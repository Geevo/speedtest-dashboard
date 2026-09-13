import {
  providerIdFromNavigation,
  providerNavigationItem,
  type NavigationItem,
  type StaticNavigationItem,
} from './navigation.ts'

const paths: Record<StaticNavigationItem, string> = {
  overview: '/',
  results: '/results',
  statistics: '/statistics',
  schedules: '/schedules',
  settings: '/settings',
}

export type ResolvedNavigation = {
  item: NavigationItem
  canonicalPath: string
  shouldRedirect: boolean
}

export function pathForNavigation(item: NavigationItem) {
  const providerId = providerIdFromNavigation(item)
  return providerId === null ? paths[item as StaticNavigationItem] : `/providers/${encodeURIComponent(providerId)}`
}

export function resolveNavigation(pathname: string): ResolvedNavigation {
  const normalized = normalizePath(pathname)
  if (normalized === '/history') {
    return { item: 'results', canonicalPath: paths.results, shouldRedirect: true }
  }

  const match = (Object.entries(paths) as [StaticNavigationItem, string][])
    .find(([, path]) => path === normalized)
  if (match) {
    return { item: match[0], canonicalPath: match[1], shouldRedirect: normalized !== pathname }
  }

  const providerMatch = normalized.match(/^\/providers\/([a-z][a-z0-9-]{0,31})$/)
  if (providerMatch) {
    return {
      item: providerNavigationItem(providerMatch[1]),
      canonicalPath: normalized,
      shouldRedirect: normalized !== pathname,
    }
  }

  const legacyProviderMatch = normalized.match(/^\/([a-z][a-z0-9-]{0,31})$/)
  if (legacyProviderMatch) {
    const item = providerNavigationItem(legacyProviderMatch[1])
    return { item, canonicalPath: pathForNavigation(item), shouldRedirect: true }
  }

  return { item: 'overview', canonicalPath: paths.overview, shouldRedirect: true }
}

function normalizePath(pathname: string) {
  if (pathname === '/') return pathname
  return pathname.replace(/\/+$/, '') || '/'
}
