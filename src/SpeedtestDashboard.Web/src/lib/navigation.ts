export type StaticNavigationItem = 'overview' | 'results' | 'statistics' | 'schedules' | 'settings'
export type ProviderNavigationItem = `provider:${string}`
export type NavigationItem = StaticNavigationItem | ProviderNavigationItem

export function providerNavigationItem(providerId: string): ProviderNavigationItem {
  return `provider:${providerId}`
}

export function providerIdFromNavigation(item: NavigationItem): string | null {
  return item.startsWith('provider:') ? item.slice('provider:'.length) : null
}
