export type HealthResponse = {
  status: 'healthy'
  service: string
  version: string
  checkedAt: string
}

export type NetworkAddressIdentity = {
  address: string
  family: 'ipv4' | 'ipv6'
  asn: string | null
  asName: string | null
  isp: string | null
  countryCode: string | null
  countryName: string | null
  region: string | null
  city: string | null
  addressSource: string
  metadataSource: string | null
}

export type NetworkIdentityResponse = {
  state: 'complete' | 'partial' | 'unavailable'
  checkedAtUtc: string
  ipv4: NetworkAddressIdentity | null
  ipv6: NetworkAddressIdentity | null
  isStale: boolean
  warning: 'refreshFailed' | null
}

export class ApiError extends Error {
  readonly status: number
  readonly retryAfterSeconds?: number

  constructor(
    message: string,
    status: number,
    retryAfterSeconds?: number,
  ) {
    super(message)
    this.status = status
    this.retryAfterSeconds = retryAfterSeconds
  }
}

export async function getHealth(signal?: AbortSignal): Promise<HealthResponse> {
  const response = await fetch('/api/health', {
    headers: { Accept: 'application/json' },
    signal,
  })

  if (!response.ok) {
    throw new Error('The dashboard API did not respond successfully.')
  }

  return response.json() as Promise<HealthResponse>
}

export async function getNetworkIdentity(refresh = false, signal?: AbortSignal): Promise<NetworkIdentityResponse> {
  const response = await fetch(`/api/network${refresh ? '?refresh=true' : ''}`, {
    headers: { Accept: 'application/json' },
    signal,
  })

  if (!response.ok) {
    const retryAfter = response.headers.get('Retry-After')
    const retryAfterSeconds = retryAfter === null ? undefined : Number.parseInt(retryAfter, 10)
    throw new ApiError(
      response.status === 429
        ? 'Network identity was refreshed recently.'
        : 'Network identity could not be loaded.',
      response.status,
      Number.isFinite(retryAfterSeconds) ? retryAfterSeconds : undefined,
    )
  }

  return response.json() as Promise<NetworkIdentityResponse>
}
