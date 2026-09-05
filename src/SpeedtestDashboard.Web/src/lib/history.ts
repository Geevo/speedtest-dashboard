import type { NetworkIdentityResponse } from './api'
import { apiFetch } from './api'
import type { SpeedTestJobStatus, SpeedTestResult } from './speed-tests'

export type HistoryStatus = Extract<SpeedTestJobStatus, 'completed' | 'failed' | 'cancelled'>

export type HistoryListItem = {
  id: number
  jobId: string
  providerId: string
  status: HistoryStatus
  completedAtUtc: string
  serverName: string | null
  serverLocation: string | null
  downloadMbps: number | null
  uploadMbps: number | null
  latencyMilliseconds: number | null
  jitterMilliseconds: number | null
  packetLossPercent: number | null
  ipv4Address: string | null
  ipv6Address: string | null
  failure: { code: string; message: string } | null
}

export type HistoryPage = {
  items: HistoryListItem[]
  nextCursor: string | null
}

export type HistoryDetail = {
  id: number
  jobId: string
  providerId: string
  status: HistoryStatus
  queuedAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string
  requestedServerId: string | null
  result: SpeedTestResult | null
  egressIdentity: NetworkIdentityResponse | null
  failure: { code: string; message: string } | null
  providerMetadata: Record<string, unknown> | null
}

export type HistoryFilters = {
  providerId?: string
  status?: HistoryStatus
  fromUtc?: string
  toUtc?: string
  limit: number
  cursor?: string | null
}

export const historyQueryKey = (filters: HistoryFilters) => ['history', filters] as const

export async function getHistory(filters: HistoryFilters, signal?: AbortSignal): Promise<HistoryPage> {
  const query = new URLSearchParams({ limit: String(filters.limit) })
  if (filters.providerId) query.set('providerId', filters.providerId)
  if (filters.status) query.set('status', filters.status)
  if (filters.fromUtc) query.set('fromUtc', filters.fromUtc)
  if (filters.toUtc) query.set('toUtc', filters.toUtc)
  if (filters.cursor) query.set('cursor', filters.cursor)
  return requestJson<HistoryPage>(`/api/history?${query}`, { signal })
}

export async function getHistoryDetail(id: number, signal?: AbortSignal): Promise<HistoryDetail> {
  return requestJson<HistoryDetail>(`/api/history/${id}`, { signal })
}

export async function deleteHistory(id: number): Promise<void> {
  const response = await apiFetch(`/api/history/${id}`, { method: 'DELETE' })
  if (!response.ok) throw new Error('The result could not be deleted.')
}

async function requestJson<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await apiFetch(url, {
    ...init,
    headers: { Accept: 'application/json', ...init?.headers },
  })
  if (!response.ok) throw new Error('Results could not be loaded.')
  return response.json() as Promise<T>
}
