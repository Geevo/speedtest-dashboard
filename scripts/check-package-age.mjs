import { readFile, readdir } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'

const repositoryRoot = fileURLToPath(new URL('../', import.meta.url))
const minimumAgeDays = Number.parseInt(process.env.MINIMUM_RELEASE_AGE_DAYS ?? '14', 10)
const cutoff = Date.now() - minimumAgeDays * 24 * 60 * 60 * 1000

if (!Number.isInteger(minimumAgeDays) || minimumAgeDays < 0) {
  throw new Error('MINIMUM_RELEASE_AGE_DAYS must be a non-negative integer.')
}

const npmPackages = await readNpmLock()
const nugetPackages = await readNuGetLocks()
const candidates = [
  ...npmPackages.map((item) => ({ ...item, registry: 'npm' })),
  ...nugetPackages
    .filter((item) => !isMicrosoftFirstParty(item.name))
    .map((item) => ({ ...item, registry: 'nuget' })),
]

const results = await mapWithConcurrency(candidates, 8, async (item) => {
  const publishedAt = item.registry === 'npm'
    ? await getNpmPublishTime(item.name, item.version)
    : await getNuGetPublishTime(item.name, item.version)

  return { ...item, publishedAt, ageMs: Date.now() - publishedAt.getTime() }
})

const tooNew = results
  .filter((item) => item.ageMs < minimumAgeDays * 24 * 60 * 60 * 1000)
  .sort((left, right) => left.ageMs - right.ageMs)

if (tooNew.length > 0) {
  console.error(`Dependency release-age policy failed: ${tooNew.length} package(s) are newer than ${minimumAgeDays} days.`)
  for (const item of tooNew) {
    console.error(`- ${item.registry}:${item.name}@${item.version} published ${item.publishedAt.toISOString()}`)
  }
  process.exitCode = 1
} else {
  console.log(`Dependency release-age policy passed for ${results.length} resolved non-Microsoft packages (${minimumAgeDays}-day minimum).`)
  console.log(`Microsoft first-party NuGet packages exempted: ${nugetPackages.length - nugetPackages.filter((item) => !isMicrosoftFirstParty(item.name)).length}.`)
}

async function readNpmLock() {
  const lockPath = `${repositoryRoot}src/SpeedtestDashboard.Web/package-lock.json`
  const lock = JSON.parse(await readFile(lockPath, 'utf8'))
  const packages = new Map()

  for (const [path, details] of Object.entries(lock.packages ?? {})) {
    if (!path || !details.version || !path.includes('node_modules/')) continue
    const name = path.split('node_modules/').at(-1)
    assertExactVersion('npm', name, details.version)
    packages.set(`${name}@${details.version}`, { name, version: details.version })
  }

  return [...packages.values()]
}

async function readNuGetLocks() {
  const lockPaths = await findFiles(repositoryRoot, 'packages.lock.json')
  const packages = new Map()

  for (const lockPath of lockPaths) {
    const lock = JSON.parse(await readFile(lockPath, 'utf8'))
    for (const framework of Object.values(lock.dependencies ?? {})) {
      for (const [name, details] of Object.entries(framework)) {
        if (details.type === 'Project') continue
        const version = details.resolved
        assertExactVersion('NuGet', name, version)
        packages.set(`${name.toLowerCase()}@${version}`, { name, version })
      }
    }
  }

  return [...packages.values()]
}

async function findFiles(directory, filename) {
  const matches = []
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (entry.name === 'node_modules' || entry.name === 'bin' || entry.name === 'obj' || entry.name === '.git') continue
    const path = `${directory}${entry.name}${entry.isDirectory() ? '/' : ''}`
    if (entry.isDirectory()) matches.push(...await findFiles(path, filename))
    else if (entry.name === filename) matches.push(path)
  }
  return matches
}

function assertExactVersion(registry, name, version) {
  if (typeof version !== 'string' || !/^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$/.test(version)) {
    throw new Error(`${registry} dependency ${name} must resolve to an exact semantic version; received ${version}.`)
  }
}

function isMicrosoftFirstParty(name) {
  return /^(Microsoft|System)\./.test(name) || name === 'NETStandard.Library'
}

async function getNpmPublishTime(name, version) {
  const response = await fetch(`https://registry.npmjs.org/${encodeURIComponent(name)}`)
  if (!response.ok) throw new Error(`npm metadata request failed for ${name}: HTTP ${response.status}`)
  const metadata = await response.json()
  const published = metadata.time?.[version]
  if (!published) throw new Error(`npm did not return a publish timestamp for ${name}@${version}.`)
  return new Date(published)
}

async function getNuGetPublishTime(name, version) {
  const id = name.toLowerCase()
  const normalizedVersion = version.toLowerCase()
  const response = await fetch(`https://api.nuget.org/v3/registration5-semver1/${id}/${normalizedVersion}.json`)
  if (!response.ok) throw new Error(`NuGet metadata request failed for ${name}@${version}: HTTP ${response.status}`)
  const registration = await response.json()
  const catalogEntry = typeof registration.catalogEntry === 'string'
    ? await fetch(registration.catalogEntry).then((result) => result.json())
    : registration.catalogEntry
  if (!catalogEntry?.published) throw new Error(`NuGet did not return a publish timestamp for ${name}@${version}.`)
  return new Date(catalogEntry.published)
}

async function mapWithConcurrency(items, limit, mapper) {
  const results = new Array(items.length)
  let nextIndex = 0

  async function worker() {
    while (nextIndex < items.length) {
      const index = nextIndex++
      results[index] = await mapper(items[index])
    }
  }

  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker))
  return results
}
