import { apiError, apiFetch } from './api'

export type ScheduleRecurrenceKind = 'oneOff' | 'interval' | 'daily' | 'weekly'

export type Schedule = {
  id: string
  name: string
  providerId: string
  serverId: string | null
  recurrenceKind: ScheduleRecurrenceKind
  runAtUtc: string | null
  intervalMinutes: number | null
  timeOfDayMinutes: number | null
  dayOfWeek: number | null
  timeZoneId: string
  enabled: boolean
  completed: boolean
  createdAtUtc: string
  updatedAtUtc: string
  lastRunAtUtc: string | null
  nextRunAtUtc: string | null
  lastJobId: string | null
  lastRunStatus: string | null
}

export type ScheduleDraft = {
  name: string
  providerId: string
  serverId?: string | null
  recurrenceKind: ScheduleRecurrenceKind
  runAtLocal?: string | null
  intervalMinutes?: number | null
  timeOfDayMinutes?: number | null
  dayOfWeek?: number | null
  timeZoneId: string
  enabled: boolean
}

export async function getSchedules(signal?: AbortSignal): Promise<Schedule[]> {
  const response = await apiFetch('/api/schedules', { headers: { Accept: 'application/json' }, signal })
  if (!response.ok) throw await apiError(response, 'Schedules could not be loaded.')
  return response.json() as Promise<Schedule[]>
}

export async function createSchedule(draft: ScheduleDraft): Promise<Schedule> {
  const response = await apiFetch('/api/schedules', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(draft),
  })
  if (!response.ok) throw await apiError(response, 'The schedule could not be created.')
  return response.json() as Promise<Schedule>
}

export async function updateSchedule(id: string, draft: ScheduleDraft): Promise<Schedule> {
  const response = await apiFetch(`/api/schedules/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(draft),
  })
  if (!response.ok) throw await apiError(response, 'The schedule could not be saved.')
  return response.json() as Promise<Schedule>
}

export async function deleteSchedule(id: string): Promise<void> {
  const response = await apiFetch(`/api/schedules/${id}`, { method: 'DELETE' })
  if (!response.ok) throw await apiError(response, 'The schedule could not be deleted.')
}

export async function setScheduleEnabled(id: string, enabled: boolean): Promise<Schedule> {
  const response = await apiFetch(`/api/schedules/${id}/${enabled ? 'enable' : 'disable'}`, { method: 'POST' })
  if (!response.ok) throw await apiError(response, 'The schedule could not be updated.')
  return response.json() as Promise<Schedule>
}

const dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']

export function describeRecurrence(schedule: Schedule): string {
  switch (schedule.recurrenceKind) {
    case 'oneOff':
      if (schedule.lastRunStatus === 'skipped') return 'Missed'
      if (schedule.lastRunStatus === 'failed') return 'Failed'
      return schedule.completed ? 'Submitted once' : 'Runs once'
    case 'interval':
      return `Every ${schedule.intervalMinutes} minute${schedule.intervalMinutes === 1 ? '' : 's'}`
    case 'daily':
      return `Every day at ${formatTimeOfDay(schedule.timeOfDayMinutes)}`
    case 'weekly':
      return `Every ${dayNames[schedule.dayOfWeek ?? 0]} at ${formatTimeOfDay(schedule.timeOfDayMinutes)}`
    default:
      return 'Unknown schedule'
  }
}

export function formatTimeOfDay(minutes: number | null): string {
  if (minutes === null) return '--:--'
  const hours = Math.floor(minutes / 60)
  const mins = minutes % 60
  return `${hours.toString().padStart(2, '0')}:${mins.toString().padStart(2, '0')}`
}

export function formatInTimeZone(value: string | null, timeZoneId: string): string {
  if (value === null) return 'Not scheduled'
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: timeZoneId,
  }).format(new Date(value))
}

const fallbackTimeZones = [
  'UTC', 'Europe/London', 'Europe/Berlin', 'Europe/Moscow',
  'America/New_York', 'America/Chicago', 'America/Denver', 'America/Los_Angeles',
  'Asia/Tokyo', 'Asia/Shanghai', 'Asia/Kolkata', 'Australia/Sydney',
]

export function listTimeZones(): string[] {
  try {
    const supported = (Intl as unknown as { supportedValuesOf?: (key: string) => string[] }).supportedValuesOf?.('timeZone')
    return supported && supported.length > 0
      ? ['UTC', ...supported.filter((zone) => zone !== 'UTC')]
      : fallbackTimeZones
  } catch {
    return fallbackTimeZones
  }
}

export function guessLocalTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone
  } catch {
    return 'UTC'
  }
}

/** Converts a wall-clock instant in `timeZoneId` to the `datetime-local` input value that displays it. */
export function toLocalInputValue(utcIso: string, timeZoneId: string): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: timeZoneId,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(new Date(utcIso))
  const get = (type: string) => parts.find((part) => part.type === type)?.value ?? '00'
  return `${get('year')}-${get('month')}-${get('day')}T${get('hour')}:${get('minute')}`
}
