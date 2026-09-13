import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { createPortal } from 'react-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { CalendarClock, ChevronLeft, ChevronRight, Clock3, ExternalLink, Pencil, Plus, Trash2, X } from 'lucide-react'
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
  const providers = useQuery({ queryKey: ['providers'], queryFn: ({ signal }) => getProviders(signal) })
  const [editing, setEditing] = useState<'new' | Schedule | null>(null)

  const invalidate = () => void queryClient.invalidateQueries({ queryKey: ['schedules'] })

  return (
    <div className="page-enter">
      <header className="mb-8 flex flex-wrap items-end justify-between gap-4 border-b border-line pb-6">
        <h1 className="text-[clamp(2rem,5vw,3.5rem)] font-semibold leading-none tracking-[-0.055em]">Schedules</h1>
        <Button type="button" onClick={() => setEditing('new')}>
          <Plus className="mr-1.5 size-4" aria-hidden="true" />
          Create schedule
        </Button>
      </header>

      {schedules.data?.length ? (
        <div className="space-y-3">
          {schedules.data.map((schedule) => (
            <ScheduleCard
              key={schedule.id}
              schedule={schedule}
              providerName={providers.data?.find((provider) => provider.id === schedule.providerId)?.displayName ?? schedule.providerId}
              onEdit={() => setEditing(schedule)}
              onChanged={invalidate}
            />
          ))}
        </div>
      ) : (
        <p className="border-b border-line py-10 text-center text-sm text-ink-muted">
          {schedules.isLoading ? 'Loading schedules…' : 'No schedules yet. Create one to run tests automatically.'}
        </p>
      )}

      {editing && (
        <ScheduleDialog
          schedule={editing === 'new' ? null : editing}
          onSaved={() => {
            setEditing(null)
            invalidate()
          }}
          onCancel={() => setEditing(null)}
        />
      )}
    </div>
  )
}

function ScheduleCard({ schedule, providerName, onEdit, onChanged }: { schedule: Schedule; providerName: string; onEdit: () => void; onChanged: () => void }) {
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
            {providerName}
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

function ScheduleDialog({ schedule, onSaved, onCancel }: {
  schedule: Schedule | null
  onSaved: () => void
  onCancel: () => void
}) {
  const dialogRef = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = dialogRef.current
    if (!dialog) return
    const opener = document.activeElement
    dialog.showModal()
    return () => {
      dialog.close()
      if (opener instanceof HTMLElement) requestAnimationFrame(() => opener.focus())
    }
  }, [])

  const dialog = (
    <dialog
      ref={dialogRef}
      className="schedule-dialog m-auto max-h-[min(92dvh,56rem)] w-[min(46rem,calc(100%-1.5rem))] overflow-hidden rounded-2xl border border-line bg-paper p-0 text-ink shadow-2xl"
      aria-labelledby="schedule-dialog-title"
      onCancel={(event) => {
        event.preventDefault()
        onCancel()
      }}
      onClick={(event) => {
        if (event.target === event.currentTarget) onCancel()
      }}
    >
      <ScheduleForm schedule={schedule} onSaved={onSaved} onCancel={onCancel} />
    </dialog>
  )

  return createPortal(dialog, document.body)
}

function ScheduleForm({ schedule, onSaved, onCancel }: {
  schedule: Schedule | null
  onSaved: () => void
  onCancel: () => void
}) {
  const providers = useQuery({ queryKey: ['providers'], queryFn: ({ signal }) => getProviders(signal) })
  const timeZones = listTimeZones()

  const initialRunAt = schedule?.runAtUtc ? toLocalInputValue(schedule.runAtUtc, schedule.timeZoneId) : ''

  const [name, setName] = useState(schedule?.name ?? '')
  const [providerId, setProviderId] = useState(schedule?.providerId ?? '')
  const [serverId, setServerId] = useState(schedule?.serverId ?? '')
  const [recurrenceKind, setRecurrenceKind] = useState<ScheduleRecurrenceKind>(schedule?.recurrenceKind ?? 'interval')
  const [timeZoneId, setTimeZoneId] = useState(schedule?.timeZoneId ?? guessLocalTimeZone())
  const [runDate, setRunDate] = useState(initialRunAt.slice(0, 10))
  const [runTime, setRunTime] = useState(initialRunAt.slice(11, 16) || '09:00')
  const [intervalMinutes, setIntervalMinutes] = useState(schedule?.intervalMinutes ?? 30)
  const [timeOfDay, setTimeOfDay] = useState(
    schedule?.timeOfDayMinutes != null ? formatTimeOfDay(schedule.timeOfDayMinutes) : '03:00',
  )
  const [dayOfWeek, setDayOfWeek] = useState(schedule?.dayOfWeek ?? 1)
  const [enabled, setEnabled] = useState(schedule?.enabled ?? true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const effectiveProviderId = providerId || providers.data?.[0]?.id || ''
  const effectiveProvider = providers.data?.find((provider) => provider.id === effectiveProviderId)
  const supportsServerSelection = effectiveProvider?.capabilities.includes('serverSelection') ?? false

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (recurrenceKind === 'oneOff' && !runDate) {
      setError('Choose a date for this one-off test.')
      return
    }
    setSaving(true)
    setError(null)
    try {
      const [hours, minutes] = timeOfDay.split(':').map(Number)
      const draft: ScheduleDraft = {
        name: name.trim(),
        providerId: effectiveProviderId,
        serverId: serverId.trim() || null,
        recurrenceKind,
        runAtLocal: recurrenceKind === 'oneOff' ? `${runDate}T${runTime}` : null,
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
    <form className="flex max-h-[min(92dvh,56rem)] flex-col" onSubmit={submit}>
      <header className="flex shrink-0 items-start justify-between gap-6 border-b border-line px-5 py-5 sm:px-7">
        <div>
          <h2 id="schedule-dialog-title" className="text-xl font-semibold tracking-[-0.03em]">
            {schedule ? 'Edit schedule' : 'Create schedule'}
          </h2>
          <p className="mt-1 text-sm text-ink-muted">Set when the backend should run this speed test.</p>
        </div>
        <Button type="button" variant="ghost" size="icon" className="-mr-2 -mt-2 shrink-0" aria-label="Close dialog" onClick={onCancel}>
          <X className="size-5" aria-hidden="true" />
        </Button>
      </header>

      <div className="space-y-5 overflow-y-auto px-5 py-6 sm:px-7">
        <label className="block text-sm font-semibold">Name
          <input autoFocus className={inputClass} value={name} onChange={(event) => setName(event.target.value)} required maxLength={120} />
        </label>

        <div className="grid gap-4 sm:grid-cols-2">
          <label className="block text-sm font-semibold">Provider
            <select className={inputClass} value={effectiveProviderId} onChange={(event) => {
              setProviderId(event.target.value)
              const selected = providers.data?.find((provider) => provider.id === event.target.value)
              if (!selected?.capabilities.includes('serverSelection')) setServerId('')
            }} required>
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
              placeholder={supportsServerSelection ? 'Automatic' : 'Chosen by provider'}
              disabled={!supportsServerSelection}
              maxLength={128}
            />
          </label>
        </div>

        {effectiveProvider?.disclosures.map((disclosure) => (
          <aside key={`${disclosure.kind}:${disclosure.url ?? disclosure.message}`} className="border-l-2 border-signal bg-signal-soft px-4 py-3 text-sm leading-6 text-ink-muted">
            {disclosure.message}{' '}
            {disclosure.url && <a className="inline-flex items-center gap-1 font-semibold text-ink underline underline-offset-4" href={disclosure.url} target="_blank" rel="noreferrer">
              {disclosure.kind === 'privacy' ? 'Privacy details' : 'Learn more'} <ExternalLink className="size-3.5" aria-hidden="true" />
            </a>}
          </aside>
        ))}

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
          <div className="grid gap-5 md:grid-cols-[minmax(0,1fr)_12rem] md:items-start">
            <CalendarPicker value={runDate} timeZoneId={timeZoneId} onChange={setRunDate} />
            <div>
              <label className="block text-sm font-semibold" htmlFor="schedule-run-time">Time</label>
              <div className="relative mt-2">
                <Clock3 className="pointer-events-none absolute left-3.5 top-1/2 size-4 -translate-y-1/2 text-ink-muted" aria-hidden="true" />
                <input
                  id="schedule-run-time"
                  type="time"
                  className="h-11 w-full rounded-lg border border-line bg-paper pl-10 pr-3 text-base outline-none transition focus:border-signal focus:ring-3 focus:ring-signal/15"
                  value={runTime}
                  onChange={(event) => setRunTime(event.target.value)}
                  required
                />
              </div>
              <p className="mt-2 text-xs leading-5 text-ink-muted">Time shown in {timeZoneId}.</p>
            </div>
          </div>
        )}

        {recurrenceKind === 'interval' && (
          <label className="block text-sm font-semibold">Every
            <div className="mt-2 flex items-center gap-2">
              <input
                type="number"
                className="h-11 min-w-0 flex-1 rounded-lg border border-line bg-paper px-3.5 text-base outline-none transition focus:border-signal focus:ring-3 focus:ring-signal/15"
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

        {error && <p role="status" className="rounded-lg bg-danger-soft px-3.5 py-3 text-sm text-danger">{error}</p>}
      </div>

      <footer className="flex shrink-0 flex-col-reverse gap-2 border-t border-line bg-paper px-5 py-4 sm:flex-row sm:justify-end sm:px-7">
        <Button type="button" variant="ghost" onClick={onCancel}>Cancel</Button>
        <Button type="submit" disabled={saving || !effectiveProviderId}>{saving ? 'Saving…' : 'Save schedule'}</Button>
      </footer>
    </form>
  )
}

function CalendarPicker({ value, timeZoneId, onChange }: {
  value: string
  timeZoneId: string
  onChange: (value: string) => void
}) {
  const today = dateKeyInTimeZone(new Date(), timeZoneId)
  const initialDate = parseDateKey(value || today)
  const [visibleMonth, setVisibleMonth] = useState(() => ({ year: initialDate.year, month: initialDate.month }))
  const days = useMemo(
    () => calendarDays(visibleMonth.year, visibleMonth.month),
    [visibleMonth.year, visibleMonth.month],
  )
  const monthLabel = new Intl.DateTimeFormat(undefined, { month: 'long', year: 'numeric', timeZone: 'UTC' })
    .format(new Date(Date.UTC(visibleMonth.year, visibleMonth.month, 1)))

  const moveMonth = (amount: number) => {
    const next = new Date(Date.UTC(visibleMonth.year, visibleMonth.month + amount, 1))
    setVisibleMonth({ year: next.getUTCFullYear(), month: next.getUTCMonth() })
  }

  const choose = (date: CalendarDay) => {
    onChange(date.key)
    if (!date.inMonth) setVisibleMonth({ year: date.year, month: date.month })
  }

  return (
    <fieldset>
      <legend className="text-sm font-semibold">Date</legend>
      <div className="mt-2 rounded-xl border border-line bg-canvas/55 p-3 sm:p-4">
        <div className="mb-3 flex items-center justify-between gap-2">
          <Button type="button" variant="ghost" size="icon" className="size-10 min-h-10" aria-label="Previous month" onClick={() => moveMonth(-1)}>
            <ChevronLeft className="size-4" aria-hidden="true" />
          </Button>
          <div className="text-sm font-semibold" aria-live="polite">{monthLabel}</div>
          <Button type="button" variant="ghost" size="icon" className="size-10 min-h-10" aria-label="Next month" onClick={() => moveMonth(1)}>
            <ChevronRight className="size-4" aria-hidden="true" />
          </Button>
        </div>
        <div className="grid grid-cols-7 text-center" aria-hidden="true">
          {['S', 'M', 'T', 'W', 'T', 'F', 'S'].map((day, index) => (
            <span key={`${day}-${index}`} className="pb-1.5 text-[11px] font-semibold text-ink-muted">{day}</span>
          ))}
        </div>
        <div className="grid grid-cols-7 gap-0.5" role="grid" aria-label={monthLabel}>
          {days.map((date) => {
            const selected = date.key === value
            const isToday = date.key === today
            return (
              <button
                key={date.key}
                type="button"
                role="gridcell"
                aria-selected={selected}
                aria-label={`${formatCalendarDate(date.key)}${isToday ? ', today' : ''}`}
                onClick={() => choose(date)}
                className={cn(
                  'relative aspect-square min-w-0 rounded-lg text-sm font-medium transition-colors focus-visible:z-10 focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-signal/25',
                  !date.inMonth && 'text-ink-muted/55',
                  date.inMonth && !selected && 'hover:bg-line/70',
                  selected && 'bg-signal text-white hover:bg-signal',
                  isToday && !selected && 'font-bold text-signal',
                )}
              >
                {date.day}
                {isToday && <span className={cn('absolute bottom-1 left-1/2 size-1 -translate-x-1/2 rounded-full bg-signal', selected && 'bg-white')} />}
              </button>
            )
          })}
        </div>
        <div className="mt-3 flex min-h-7 items-center justify-between gap-3 border-t border-line pt-3">
          <span className="truncate text-xs text-ink-muted">{value ? formatCalendarDate(value) : 'Choose a date'}</span>
          <button
            type="button"
            className="shrink-0 text-xs font-semibold text-signal hover:underline"
            onClick={() => {
              const date = parseDateKey(today)
              onChange(today)
              setVisibleMonth({ year: date.year, month: date.month })
            }}
          >
            Today
          </button>
        </div>
      </div>
    </fieldset>
  )
}

type CalendarDay = {
  key: string
  year: number
  month: number
  day: number
  inMonth: boolean
}

function calendarDays(year: number, month: number): CalendarDay[] {
  const firstWeekday = new Date(Date.UTC(year, month, 1)).getUTCDay()
  return Array.from({ length: 42 }, (_, index) => {
    const date = new Date(Date.UTC(year, month, index - firstWeekday + 1))
    const dateYear = date.getUTCFullYear()
    const dateMonth = date.getUTCMonth()
    const day = date.getUTCDate()
    return {
      key: `${dateYear}-${String(dateMonth + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`,
      year: dateYear,
      month: dateMonth,
      day,
      inMonth: dateYear === year && dateMonth === month,
    }
  })
}

function parseDateKey(value: string): { year: number; month: number } {
  const [year, month] = value.split('-').map(Number)
  return { year, month: month - 1 }
}

function dateKeyInTimeZone(date: Date, timeZoneId: string): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: timeZoneId,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(date)
  const part = (type: string) => parts.find((item) => item.type === type)?.value ?? ''
  return `${part('year')}-${part('month')}-${part('day')}`
}

function formatCalendarDate(value: string): string {
  const [year, month, day] = value.split('-').map(Number)
  return new Intl.DateTimeFormat(undefined, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(Date.UTC(year, month - 1, day)))
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
      className="grid h-11 w-12 shrink-0 place-items-center rounded-lg focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-signal/25 disabled:cursor-not-allowed disabled:opacity-50"
    >
      <span
        aria-hidden="true"
        className={cn(
          'relative h-6 w-11 rounded-full transition-colors',
          checked ? 'bg-signal' : 'bg-line',
        )}
      >
        <span className={cn('absolute left-0.5 top-0.5 size-5 rounded-full bg-paper shadow-sm transition-transform', checked && 'translate-x-5')} />
      </span>
    </button>
  )
}
