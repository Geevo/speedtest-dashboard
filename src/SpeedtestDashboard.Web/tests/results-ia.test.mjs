import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import { pathForNavigation, resolveNavigation } from '../src/lib/routes.ts'

const source = (path) => readFile(new URL(path, import.meta.url), 'utf8')

test('results is canonical and legacy history bookmarks redirect', () => {
  assert.equal(pathForNavigation('results'), '/results')
  assert.deepEqual(resolveNavigation('/results'), {
    item: 'results',
    canonicalPath: '/results',
    shouldRedirect: false,
  })
  assert.deepEqual(resolveNavigation('/history'), {
    item: 'results',
    canonicalPath: '/results',
    shouldRedirect: true,
  })
})

test('results retains record controls and contains no analytics charts', async () => {
  const results = await source('../src/components/results-page.tsx')

  assert.match(results, /Results filters/)
  assert.match(results, /Provider/)
  assert.match(results, /Status/)
  assert.match(results, /Time range/)
  assert.match(results, /ResultsRecords/)
  assert.match(results, /ResultDetailPanel/)
  assert.match(results, /Delete record/)
  assert.doesNotMatch(results, /from 'recharts'/)
  assert.doesNotMatch(results, /getStatistics/)
  assert.doesNotMatch(results, /ReferenceLine|AreaChart|LineChart|ChartPanel/)
})

test('results filters do not add a duplicate divider above the results region', async () => {
  const results = await source('../src/components/results-page.tsx')
  const filters = results.match(/<section aria-label="Results filters" className="([^"]+)"/)

  assert.ok(filters)
  assert.doesNotMatch(filters[1], /\bborder-b\b/)
})

test('overview provider states share one row when all three providers are present', async () => {
  const overview = await source('../src/components/overview-page.tsx')
  const providerGrid = overview.match(/providers\.data \? \(\s*<div className="([^"]+)"/)

  assert.ok(providerGrid)
  assert.match(providerGrid[1], /\bsm:grid-cols-3\b/)
  assert.match(providerGrid[1], /\bdivide-y\b/)
  assert.match(providerGrid[1], /\bsm:divide-y-0\b/)
})

test('statistics remains the sole owner of aggregates and charts', async () => {
  const statistics = await source('../src/components/statistics-page.tsx')
  const shell = await source('../src/components/app-shell.tsx')

  assert.match(statistics, /from 'recharts'/)
  assert.match(statistics, /Throughput over time/)
  assert.match(statistics, /Latency and jitter/)
  assert.match(statistics, /ReferenceLine/)
  assert.match(statistics, /Provider comparison/)
  assert.match(shell, /id: 'results' as const, label: 'Results'/)
  assert.doesNotMatch(shell, /label: 'History'/)
})

test('LibreSpeed, FAST.com, and Ookla keep their intended provider order', async () => {
  const shell = await source('../src/components/app-shell.tsx')
  const results = await source('../src/components/results-page.tsx')
  const statistics = await source('../src/components/statistics-page.tsx')

  for (const providerUi of [shell, results, statistics]) {
    assert.ok(providerUi.indexOf('LibreSpeed') < providerUi.indexOf('FAST.com'))
    assert.ok(providerUi.indexOf('FAST.com') < providerUi.indexOf('Ookla'))
  }
})
