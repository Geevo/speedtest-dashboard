import { useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { CalendarClock, Pencil, Plus, Trash2 } from 'lucide-react'
import { ApiError } from '../lib/api'
import {
  createSchedule,
  deleteSchedule,
  describeRecurrence,
  formatInTimeZone,
  formatTimeOfDay,
  getSchedules,
  guessLocalTimeZone,
  listTimeZones,
  setScheduleEnabled,
  toLocalInputValue,
  updateSchedule,
  type Schedule,
  type ScheduleDraft,
  type ScheduleRecurrenceKind,
} from '../lib/schedules'
import { getProviders } from '../lib/speed-tests'
import { cn } from '../lib/utils'
import { Button } from './ui/button'

const inputClass = 'mt-2 h-11 w-full rounded-lg border border-line bg-paper px-3.5 text-base outline-none transition focus:border-signal focus:ring-3 focus:ring-signal/15'

const dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']

export function SchedulesPage() {
  const queryClient = useQueryClient()
  const schedules = useQuery({ queryKey: ['schedules'], queryFn: ({ signal }) => getSchedules(signal) })
  const [editing, setEditing] = useState<'new' | Schedule | null>(null)

  const invalidate = () => void queryClient.invalidateQueries({ queryKey: ['schedules'] })

  return (
    <div className="page-enter">
      <header className="mb-8 flex flex-wrap items-end justify-between gap-4 border-b border-line pb-6">
        <h1 className="text-[clamp(2rem,5vw,3.5rem)] font-semibold leading-none tracking-[-0.055em]">Schedules</h1>
        {!editing && (
          <Button type="button" onClick={() => setEditing('new')}>
            <Plus className="mr-1.5 size-4" aria-hidden="true" />
            Create schedule
          </Button>
        )}
      </header>

      {editing ? (
        <ScheduleForm
          schedule={editing === 'new' ? null : editing}
          onSaved={() => {
            setEditing(null)
            invalidate()
          }}
          onCancel={() => setEditing(null)}
        />
      ) : schedules.data?.length ? (
        <div className="space-y-3">
          {schedules.data.map((schedule) => (
            <ScheduleCard
              key={schedule.id}
              schedule={schedule}
              onEdit={() => setEditing(schedule)}
              onChanged={invalidate}
            />
          ))}
        </div>
      ) : (
        <p className="border-y border-line py-10 text-center text-sm text-ink-muted">
          {schedules.isLoading ? 'Loading schedules…' : 'No schedules yet. Create one to run tests automatically.'}
        </p>
      )}
    </div>
  )
}

function ScheduleCard({ schedule, onEdit, onChanged }: { schedule: Schedule; onEdit: () => void; onChanged: () => void }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const toggle = async () => {
    setBusy(true)
    setError(null)
    try {
      await setScheduleEnabled(schedule.id, !schedule.enabled)
      onChanged()
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'The schedule could not be updated.')
    } finally {
      setBusy(false)
    }
  }

  const remove = async () => {
    if (!window.confirm(`Delete "${schedule.name}"? This cannot be undone.`)) return
    setBusy(true)
    setError(null)
    try {
      await deleteSchedule(schedule.id)
      onChanged()
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'The schedule could not be deleted.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="rounded-xl border border-line bg-paper p-5">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <CalendarClock className="size-4 shrink-0 text-ink-muted" aria-hidden="true" />
            <h3 className="break-words font-semibold">{schedule.name}</h3>
            {!schedule.enabled && <Badge>Disabled</Badge>}
            {schedule.recurrenceKind === 'oneOff' && schedule.lastRunStatus === 'skipped'
              ? <Badge>Missed</Badge>
              : schedule.recurrenceKind === 'oneOff' && schedule.lastRunStatus === 'failed'
                ? <Badge>Failed</Badge>
                : schedule.completed && <Badge>Submitted</Badge>}
          </div>
          <p className="mt-1 text-sm capitalize text-ink-muted">
            {schedule.providerId}
            {schedule.serverId ? ` · Server ${schedule.serverId}` : ' · Automatic server'}
          </p>
          <p className="mt-1 text-sm text-ink-muted">
            {describeRecurrence(schedule)} · {schedule.timeZoneId}
          </p>
        </div>
        <div className="flex shrink-0 gap-2">
          <Button variant="ghost" size="icon" aria-label="Edit schedule" onClick={onEdit}>
            <Pencil className="size-4" />
          </Button>
          <Button variant="ghost" size="icon" aria-label="Delete schedule" disabled={busy} onClick={() => void remove()}>
            <Trash2 className="size-4" />
          </Button>
        </div>
      </div>
      <div className="mt-4 grid grid-cols-2 gap-4 border-t border-line pt-4 text-sm sm:grid-cols-3">
        <div>
          <div className="text-xs font-semibold text-ink-muted">Next run</div>
          <div className="mt-0.5">{formatInTimeZone(schedule.nextRunAtUtc, schedule.timeZoneId)}</div>
        </div>
        <div>
          <div className="text-xs font-semibold text-ink-muted">Last run</div>
          <div className="mt-0.5 capitalize">{schedule.lastRunStatus ?? 'Never'}</div>
        </div>
        <div className="flex items-center justify-between gap-3 sm:justify-start sm:gap-3">
          <span className="text-xs font-semibold text-ink-muted">Enabled</span>
          <Switch
            checked={schedule.enabled}
            disabled={busy || schedule.completed}
            label={schedule.enabled ? 'Disable schedule' : 'Enable schedule'}
            onChange={() => void toggle()}
          />
        </div>
      </div>
      {error && <p role="status" className="mt-3 text-sm text-danger">{error}</p>}
    </div>
  )
}

function ScheduleForm({ schedule, onSaved, onCancel }: {
  schedule: Schedule | null
  onSaved: () => void
  onCancel: () => void
}) {
  const providers = useQuery({ queryKey: ['providers'], queryFn: ({ signal }) => getProviders(signal) })
  const timeZones = listTimeZones()

  const [name, setName] = useState(schedule?.name ?? '')
  const [providerId, setProviderId] = useState(schedule?.providerId ?? '')
  const [serverId, setServerId] = useState(schedule?.serverId ?? '')
  const [recurrenceKind, setRecurrenceKind] = useState<ScheduleRecurrenceKind>(schedule?.recurrenceKind ?? 'interval')
  const [timeZoneId, setTimeZoneId] = useState(schedule?.timeZoneId ?? guessLocalTimeZone())
  const [runAtLocal, setRunAtLocal] = useState(
    schedule?.runAtUtc ? toLocalInputValue(schedule.runAtUtc, schedule.timeZoneId) : '',
  )
  const [intervalMinutes, setIntervalMinutes] = useState(schedule?.intervalMinutes ?? 30)
  const [timeOfDay, setTimeOfDay] = useState(
    schedule?.timeOfDayMinutes != null ? formatTimeOfDay(schedule.timeOfDayMinutes) : '03:00',
  )
  const [dayOfWeek, setDayOfWeek] = useState(schedule?.dayOfWeek ?? 1)
  const [enabled, setEnabled] = useState(schedule?.enabled ?? true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const effectiveProviderId = providerId || providers.data?.[0]?.id || ''

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const [hours, minutes] = timeOfDay.split(':').map(Number)
      const draft: ScheduleDraft = {
        name: name.trim(),
        providerId: effectiveProviderId,
        serverId: serverId.trim() || null,
        recurrenceKind,
        runAtLocal: recurrenceKind === 'oneOff' ? runAtLocal : null,
        intervalMinutes: recurrenceKind === 'interval' ? intervalMinutes : null,
        timeOfDayMinutes: recurrenceKind === 'daily' || recurrenceKind === 'weekly' ? hours * 60 + minutes : null,
        dayOfWeek: recurrenceKind === 'weekly' ? dayOfWeek : null,
        timeZoneId,
        enabled,
      }
      if (schedule) {
        await updateSchedule(schedule.id, draft)
      } else {
        await createSchedule(draft)
      }
      onSaved()
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'The schedule could not be saved.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <form className="max-w-2xl space-y-5 rounded-xl border border-line bg-paper p-6" onSubmit={submit}>
      <h2 className="text-xl font-semibold tracking-[-0.03em]">{schedule ? 'Edit schedule' : 'Create schedule'}</h2>

      <label className="block text-sm font-semibold">Name
        <input className={inputClass} value={name} onChange={(event) => setName(event.target.value)} required maxLength={120} />
      </label>

      <div className="grid gap-4 sm:grid-cols-2">
        <label className="block text-sm font-semibold">Provider
          <select className={inputClass} value={effectiveProviderId} onChange={(event) => setProviderId(event.target.value)} required>
            {providers.data?.map((provider) => (
              <option key={provider.id} value={provider.id}>{provider.displayName}</option>
            ))}
          </select>
        </label>
        <label className="block text-sm font-semibold">Server (optional)
          <input
            className={inputClass}
            value={serverId}
            onChange={(event) => setServerId(event.target.value)}
            placeholder="Automatic"
            maxLength={128}
          />
        </label>
      </div>

      <label className="block text-sm font-semibold">Schedule type
        <select
          className={inputClass}
          value={recurrenceKind}
          onChange={(event) => setRecurrenceKind(event.target.value as ScheduleRecurrenceKind)}
        >
          <option value="oneOff">One-off</option>
          <option value="interval">Every X minutes/hours</option>
          <option value="daily">Daily</option>
          <option value="weekly">Weekly</option>
        </select>
      </label>

      {recurrenceKind === 'oneOff' && (
        <label className="block text-sm font-semibold">Run at
          <input
            type="datetime-local"
            className={inputClass}
            value={runAtLocal}
            onChange={(event) => setRunAtLocal(event.target.value)}
            required
          />
        </label>
      )}

      {recurrenceKind === 'interval' && (
        <label className="block text-sm font-semibold">Every
          <div className="mt-2 flex items-center gap-2">
            <input
              type="number"
              className={inputClass}
              min={1}
              max={10080}
              value={intervalMinutes}
              onChange={(event) => setIntervalMinutes(Number(event.target.value))}
              required
            />
            <span className="shrink-0 text-sm text-ink-muted">minutes</span>
          </div>
        </label>
      )}

      {(recurrenceKind === 'daily' || recurrenceKind === 'weekly') && (
        <div className={cn('grid gap-4', recurrenceKind === 'weekly' && 'sm:grid-cols-2')}>
          {recurrenceKind === 'weekly' && (
            <label className="block text-sm font-semibold">Day of week
              <select className={inputClass} value={dayOfWeek} onChange={(event) => setDayOfWeek(Number(event.target.value))}>
                {dayNames.map((day, index) => (
                  <option key={day} value={index}>{day}</option>
                ))}
              </select>
            </label>
          )}
          <label className="block text-sm font-semibold">Time of day
            <input
              type="time"
              className={inputClass}
              value={timeOfDay}
              onChange={(event) => setTimeOfDay(event.target.value)}
              required
            />
          </label>
        </div>
      )}

      <label className="block text-sm font-semibold">Time zone
        <select className={inputClass} value={timeZoneId} onChange={(event) => setTimeZoneId(event.target.value)}>
          {timeZones.map((zone) => (
            <option key={zone} value={zone}>{zone}</option>
          ))}
        </select>
      </label>

      <div className="flex items-center justify-between gap-3 border-t border-line pt-4">
        <span className="text-sm font-semibold">Enabled</span>
        <Switch checked={enabled} label="Enabled" onChange={setEnabled} />
      </div>

      {error && <p role="status" className="text-sm text-danger">{error}</p>}

      <div className="flex gap-2">
        <Button type="submit" disabled={saving || !effectiveProviderId}>{saving ? 'Saving…' : 'Save schedule'}</Button>
        <Button type="button" variant="ghost" onClick={onCancel}>Cancel</Button>
      </div>
    </form>
  )
}

function Badge({ children }: { children: string }) {
  return (
    <span className="rounded-full bg-line px-2 py-0.5 text-[11px] font-semibold uppercase tracking-wide text-ink-muted">
      {children}
    </span>
  )
}

function Switch({ checked, disabled = false, label, onChange }: {
  checked: boolean
  disabled?: boolean
  label: string
  onChange: (checked: boolean) => void
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={cn(
        'relative h-11 w-12 shrink-0 rounded-full transition-colors focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-signal/25 disabled:cursor-not-allowed disabled:opacity-50',
        checked ? 'bg-signal' : 'bg-line',
      )}
    >
      <span className={cn('absolute left-0 top-3 size-5 rounded-full bg-paper shadow-sm transition-transform', checked ? 'translate-x-6' : 'translate-x-1')} />
    </button>
  )
}
