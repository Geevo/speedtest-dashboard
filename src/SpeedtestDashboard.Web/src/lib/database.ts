import { apiFetch, apiError } from './api'

export type DatabaseStorage = {
  databaseBytes: number
  writeAheadLogBytes: number
  sharedMemoryBytes: number
  totalBytes: number
}

export type DatabaseCompaction = {
  before: DatabaseStorage
  after: DatabaseStorage
}

export async function getDatabaseStorage(signal?: AbortSignal): Promise<DatabaseStorage> {
  const response = await apiFetch('/api/database/storage', {
    headers: { Accept: 'application/json' },
    signal,
  })
  if (!response.ok) throw await apiError(response, 'Database storage could not be loaded.')
  return response.json() as Promise<DatabaseStorage>
}

export async function compactDatabase(): Promise<DatabaseCompaction> {
  const response = await apiFetch('/api/database/compact', {
    method: 'POST',
    headers: { Accept: 'application/json' },
  })
  if (!response.ok) throw await apiError(response, 'The database could not be compacted.')
  return response.json() as Promise<DatabaseCompaction>
}
