import type { NavigationItem } from '../components/app-shell'

const paths: Record<NavigationItem, string> = {
  overview: '/',
  results: '/results',
  statistics: '/statistics',
  schedules: '/schedules',
  ookla: '/ookla',
  librespeed: '/librespeed',
  settings: '/settings',
}

export type ResolvedNavigation = {
  item: NavigationItem
  canonicalPath: string
  shouldRedirect: boolean
}

export function pathForNavigation(item: NavigationItem) {
  return paths[item]
}

export function resolveNavigation(pathname: string): ResolvedNavigation {
  const normalized = normalizePath(pathname)
  if (normalized === '/history') {
    return { item: 'results', canonicalPath: paths.results, shouldRedirect: true }
  }

  const match = (Object.entries(paths) as [NavigationItem, string][])
    .find(([, path]) => path === normalized)
  if (match) {
    return { item: match[0], canonicalPath: match[1], shouldRedirect: normalized !== pathname }
  }

  return { item: 'overview', canonicalPath: paths.overview, shouldRedirect: true }
}

function normalizePath(pathname: string) {
  if (pathname === '/') return pathname
  return pathname.replace(/\/+$/, '') || '/'
}
