import type { NetworkAddressIdentity } from './api'

/**
 * IPConfig.io supplies region and city only when its address database can place the address,
 * so datacenter and anycast addresses usually carry country alone.
 */
export function formatLocation(identity: NetworkAddressIdentity): string | null {
  const place = [identity.city, identity.region].filter(Boolean).join(', ')
  const country = identity.countryName
    ? `${identity.countryName}${identity.countryCode ? ` · ${identity.countryCode}` : ''}`
    : ''
  return [place, country].filter(Boolean).join(' - ') || null
}
