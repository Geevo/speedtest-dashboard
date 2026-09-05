import { ApiError, type NetworkIdentityResponse } from './api'

export type ProviderCapability =
  | 'serverDiscovery'
  | 'serverSelection'
  | 'download'
  | 'upload'
  | 'latency'
  | 'jitter'
  | 'packetLoss'
  | 'resultUrl'
  | 'ipv6'

export type ProviderSummary = {
  id: string
  displayName: string
  capabilities: ProviderCapability[]
  healthState: 'available' | 'unavailable' | 'degraded'
  version: string | null
  checkedAtUtc: string
  message: string | null
}

export type SpeedTestServer = {
  providerId: string
  id: string
  name: string
  sponsor: string | null
  location: string | null
  countryCode: string | null
  host: string | null
  distanceKilometres: number | null
  latencyMilliseconds: number | null
}

export type SpeedTestJobStatus =
  | 'queued'
  | 'starting'
  | 'running'
  | 'processingResult'
  | 'completed'
  | 'failed'
  | 'cancelled'

export type SpeedTestResult = {
  providerId: string
  serverId: string | null
  serverName: string | null
  serverLocation: string | null
  downloadMbps: number | null
  uploadMbps: number | null
  latencyMilliseconds: number | null
  jitterMilliseconds: number | null
  packetLossPercent: number | null
  resultUrl: string | null
}

export type SpeedTestJob = {
  id: string
  providerId: string
  status: SpeedTestJobStatus
  stage: string
  version: number
  createdAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  egressIdentity: NetworkIdentityResponse | null
  result: SpeedTestResult | null
  failure: { code: string; message: string } | null
}

export type CreateSpeedTestRequest = {
  providerId: string
  serverId?: string | null
}

export type CreateSpeedTestResponse = {
  id: string
  providerId: string
  status: 'queued'
  stage: string
  version: number
  createdAtUtc: string
  resourceUrl: string
  eventsUrl: string
}

export type SpeedTestEvent = {
  version: number
  emittedAtUtc: string
  job: SpeedTestJob | null
}

export const speedTestJobKey = (jobId: string) => ['speed-test-job', jobId] as const

export function isTerminalJob(status: SpeedTestJobStatus) {
  return status === 'completed' || status === 'failed' || status === 'cancelled'
}

export async function getProviders(signal?: AbortSignal): Promise<ProviderSummary[]> {
  return requestJson<ProviderSummary[]>('/api/providers', { signal })
}

export async function getProvider(providerId: string, signal?: AbortSignal): Promise<ProviderSummary> {
  return requestJson<ProviderSummary>(`/api/providers/${encodeURIComponent(providerId)}`, { signal })
}

export async function getProviderServers(
  providerId: string,
  signal?: AbortSignal,
): Promise<SpeedTestServer[]> {
  return requestJson<SpeedTestServer[]>(
    `/api/providers/${encodeURIComponent(providerId)}/servers?limit=100`,
    { signal },
  )
}

export async function createSpeedTest(request: CreateSpeedTestRequest): Promise<CreateSpeedTestResponse> {
  return requestJson<CreateSpeedTestResponse>('/api/tests', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  })
}

export async function getSpeedTestJob(jobId: string, signal?: AbortSignal): Promise<SpeedTestJob> {
  return requestJson<SpeedTestJob>(`/api/tests/${encodeURIComponent(jobId)}`, { signal })
}

export async function cancelSpeedTest(jobId: string): Promise<SpeedTestJob> {
  return requestJson<SpeedTestJob>(`/api/tests/${encodeURIComponent(jobId)}/cancel`, { method: 'POST' })
}

export function subscribeToSpeedTest(
  jobId: string,
  onEvent: (event: SpeedTestEvent) => void,
  onError: () => void,
) {
  const source = new EventSource(`/api/tests/${encodeURIComponent(jobId)}/events`)
  const eventTypes = ['snapshot', 'state', 'result', 'error', 'heartbeat'] as const

  for (const eventType of eventTypes) {
    source.addEventListener(eventType, (message) => {
      try {
        onEvent(JSON.parse(message.data) as SpeedTestEvent)
      } catch {
        onError()
      }
    })
  }

  source.onerror = onError
  return () => source.close()
}

function asyncError(response: Response) {
  const retryAfter = response.headers.get('Retry-After')
  const retryAfterSeconds = retryAfter === null ? undefined : Number.parseInt(retryAfter, 10)
  return new ApiError(
    response.status === 429 ? 'The speed-test queue is full.' : 'The speed-test request failed.',
    response.status,
    Number.isFinite(retryAfterSeconds) ? retryAfterSeconds : undefined,
  )
}

async function requestJson<T>(url: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers)
  headers.set('Accept', 'application/json')
  const response = await fetch(url, {
    ...init,
    headers,
  })
  if (!response.ok) {
    throw asyncError(response)
  }

  return response.json() as Promise<T>
}
