export type HealthResponse = {
  status: 'healthy'
  service: string
  version: string
  checkedAt: string
}

export type SessionResponse = {
  mode: 'local' | 'none'
  authenticated: boolean
  user: { id: string; username: string } | null
  loginConfigured: boolean
  showDisabledWarning: boolean
}

type ProblemDetails = { detail?: string; code?: string }

let csrfToken: string | null = null

export const authExpiredEvent = 'speedtest-auth-expired'

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
  readonly code?: string

  constructor(
    message: string,
    status: number,
    retryAfterSeconds?: number,
    code?: string,
  ) {
    super(message)
    this.status = status
    this.retryAfterSeconds = retryAfterSeconds
    this.code = code
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
  const response = await apiFetch(`/api/network${refresh ? '?refresh=true' : ''}`, {
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

export async function getSession(signal?: AbortSignal): Promise<SessionResponse> {
  const response = await fetch('/api/auth/session', {
    headers: { Accept: 'application/json' },
    credentials: 'same-origin',
    signal,
  })
  if (!response.ok) throw new ApiError('The sign-in state could not be loaded.', response.status)
  return response.json() as Promise<SessionResponse>
}

export async function refreshCsrfToken(signal?: AbortSignal): Promise<string> {
  const response = await fetch('/api/auth/csrf', {
    headers: { Accept: 'application/json' },
    credentials: 'same-origin',
    signal,
  })
  if (!response.ok) throw new ApiError('Request protection could not be initialized.', response.status)
  const body = (await response.json()) as { token: string }
  csrfToken = body.token
  return body.token
}

export async function login(username: string, password: string): Promise<SessionResponse> {
  await refreshCsrfToken()
  const response = await apiFetch('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, password }),
  }, false)
  if (!response.ok) throw await apiError(response, 'Sign in failed.')
  const session = (await response.json()) as SessionResponse
  await refreshCsrfToken()
  return session
}

export async function logout(): Promise<void> {
  const response = await apiFetch('/api/auth/logout', { method: 'POST' })
  if (!response.ok) throw await apiError(response, 'Sign out failed.')
  csrfToken = null
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  const response = await apiFetch('/api/auth/change-password', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ currentPassword, newPassword }),
  })
  if (!response.ok) throw await apiError(response, 'The password could not be changed.')
  await refreshCsrfToken()
}

export async function enableLogin(username: string, password: string): Promise<SessionResponse> {
  const response = await apiFetch('/api/auth/setup', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, password }),
  }, false)
  if (!response.ok) throw await apiError(response, 'Login protection could not be enabled.')
  const session = (await response.json()) as SessionResponse
  await refreshCsrfToken()
  return session
}

export async function disableLogin(currentPassword: string): Promise<SessionResponse> {
  const response = await apiFetch('/api/auth/disable', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ currentPassword }),
  })
  if (!response.ok) throw await apiError(response, 'Login protection could not be disabled.')
  const session = (await response.json()) as SessionResponse
  await refreshCsrfToken()
  return session
}

export async function setDisabledWarningVisible(showDisabledWarning: boolean): Promise<SessionResponse> {
  const response = await apiFetch('/api/auth/preferences', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ showDisabledWarning }),
  })
  if (!response.ok) throw await apiError(response, 'The notice preference could not be saved.')
  return response.json() as Promise<SessionResponse>
}

export type ApiKeyResponse = {
  enabled: boolean
  key: string | null
  createdAtUtc: string | null
  lastUsedAtUtc: string | null
}

export async function getApiKey(signal?: AbortSignal): Promise<ApiKeyResponse> {
  const response = await apiFetch('/api/api-key', { headers: { Accept: 'application/json' }, signal })
  if (!response.ok) throw await apiError(response, 'The API key status could not be loaded.')
  return response.json() as Promise<ApiKeyResponse>
}

export async function regenerateApiKey(): Promise<ApiKeyResponse> {
  const response = await apiFetch('/api/api-key/regenerate', { method: 'POST' })
  if (!response.ok) throw await apiError(response, 'The API key could not be generated.')
  return response.json() as Promise<ApiKeyResponse>
}

export async function revokeApiKey(): Promise<void> {
  const response = await apiFetch('/api/api-key', { method: 'DELETE' })
  if (!response.ok) throw await apiError(response, 'The API key could not be revoked.')
}

export async function apiFetch(url: string, init: RequestInit = {}, handleUnauthorized = true): Promise<Response> {
  const method = (init.method ?? 'GET').toUpperCase()
  const unsafe = !['GET', 'HEAD', 'OPTIONS'].includes(method)
  if (unsafe && csrfToken === null) await refreshCsrfToken(init.signal ?? undefined)

  const headers = new Headers(init.headers)
  if (unsafe && csrfToken !== null) headers.set('X-CSRF-TOKEN', csrfToken)
  const response = await fetch(url, { ...init, headers, credentials: 'same-origin' })
  if (response.status === 401 && handleUnauthorized) {
    csrfToken = null
    window.dispatchEvent(new Event(authExpiredEvent))
  }
  return response
}

async function apiError(response: Response, fallback: string): Promise<ApiError> {
  const retryAfter = response.headers.get('Retry-After')
  const retryAfterSeconds = retryAfter === null ? undefined : Number.parseInt(retryAfter, 10)
  let problem: ProblemDetails | null = null
  try {
    problem = (await response.json()) as ProblemDetails
  } catch {
    // A proxy may return a non-JSON error page; keep the UI message sanitized.
  }
  return new ApiError(
    problem?.detail ?? fallback,
    response.status,
    Number.isFinite(retryAfterSeconds) ? retryAfterSeconds : undefined,
    problem?.code,
  )
}
