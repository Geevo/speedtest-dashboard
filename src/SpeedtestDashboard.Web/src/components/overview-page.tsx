import { useQuery } from '@tanstack/react-query'
import { ArrowDown, ArrowUp, CalendarClock, CheckCircle2, Waves } from 'lucide-react'
import { getHistory } from '../lib/history'
import { formatInTimeZone, getSchedules } from '../lib/schedules'
import { getProviders, isProviderInstalled, type ProviderSummary } from '../lib/speed-tests'
import { getStatistics, statisticsQueryKey } from '../lib/statistics'
import { NetworkIdentityPanel } from './network-identity-panel'

export function OverviewPage() {
  const latest = useQuery({
    queryKey: ['history', 'overview', 'latest-completed'],
    queryFn: ({ signal }) => getHistory({ status: 'completed', limit: 1 }, signal),
  })
  const schedules = useQuery({ queryKey: ['schedules'], queryFn: ({ signal }) => getSchedules(signal) })
  const providers = useQuery({ queryKey: ['providers'], queryFn: ({ signal }) => getProviders(signal) })
  const statistics = useQuery({ queryKey: statisticsQueryKey('7d'), queryFn: ({ signal }) => getStatistics('7d', undefined, signal) })
  const last = latest.data?.items[0]
  const visibleProviders = providers.data?.filter(isProviderInstalled)
  const showProviderSection = visibleProviders === undefined || visibleProviders.length > 0
  const nextSchedule = schedules.data
    ?.filter((schedule) => schedule.enabled && schedule.nextRunAtUtc !== null)
    .sort((a, b) => (a.nextRunAtUtc! < b.nextRunAtUtc! ? -1 : 1))[0]

  return (
    <div className="page-enter">
      <header className="mb-10 flex flex-wrap items-end justify-between gap-4 border-b border-line pb-6">
        <h1 className="text-[clamp(2rem,5vw,3.5rem)] font-semibold leading-none tracking-[-0.055em]">Overview</h1>
        {nextSchedule && (
          <div className="flex items-center gap-2 text-sm text-ink-muted">
            <CalendarClock className="size-4" aria-hidden="true" />
            <span>
              Next scheduled test <span className="font-semibold text-ink">{formatInTimeZone(nextSchedule.nextRunAtUtc, nextSchedule.timeZoneId)}</span> · {nextSchedule.providerId}
            </span>
          </div>
        )}
      </header>

      <NetworkIdentityPanel />

      <section aria-labelledby="snapshot-heading" className="mt-12">
        <div className="mb-5">
          <h2 id="snapshot-heading" className="text-2xl font-semibold tracking-[-0.035em]">7-day snapshot</h2>
          <p className="mt-1 text-sm text-ink-muted">Median performance and overall test reliability.</p>
        </div>
        <div className="grid grid-cols-2 border-y border-line lg:grid-cols-4">
          <Metric icon={ArrowDown} label="Median download" value={statistics.data?.download?.median ?? null} unit="Mbps" />
          <Metric icon={ArrowUp} label="Median upload" value={statistics.data?.upload?.median ?? null} unit="Mbps" />
          <Metric icon={Waves} label="Median latency" value={statistics.data?.latency?.median ?? null} unit="ms" />
          <Metric icon={CheckCircle2} label="Success rate" value={statistics.data?.tests.successRate ?? null} unit="%" />
        </div>
      </section>

      <section aria-labelledby="latest-heading" className="mt-12">
        <div className="mb-5 flex items-end justify-between gap-4">
          <div>
            <h2 id="latest-heading" className="text-2xl font-semibold tracking-[-0.035em]">Latest test</h2>
            {last && <p className="mt-1 text-sm text-ink-muted">{formatServer(last.serverName, last.serverLocation)}</p>}
          </div>
          {last && <time className="text-xs text-ink-muted">{formatDate(last.completedAtUtc)}</time>}
        </div>
        {last ? (
          <div className="grid grid-cols-2 border-y border-line sm:grid-cols-3">
            <Metric icon={ArrowDown} label="Download" value={last.downloadMbps} unit="Mbps" />
            <Metric icon={ArrowUp} label="Upload" value={last.uploadMbps} unit="Mbps" />
            <Metric icon={Waves} label="Latency" value={last.latencyMilliseconds} unit="ms" />
          </div>
        ) : (
          <p className="border-y border-line py-8 text-sm text-ink-muted">{latest.isLoading ? 'Loading latest test…' : 'No completed tests.'}</p>
        )}
      </section>

      {showProviderSection && (
        <section aria-labelledby="providers-heading" className="mt-12">
          <h2 id="providers-heading" className="mb-5 text-2xl font-semibold tracking-[-0.035em]">Providers</h2>
          {visibleProviders ? (
            <div className="grid gap-px border-y border-line bg-line sm:grid-cols-[repeat(auto-fit,minmax(14rem,1fr))]">
              {visibleProviders.map((provider) => <ProviderState key={provider.id} provider={provider} />)}
            </div>
          ) : (
            <p className="border-y border-line py-8 text-sm text-ink-muted">
              {providers.isError ? 'Provider status could not be loaded.' : 'Checking providers…'}
            </p>
          )}
        </section>
      )}
    </div>
  )
}

function Metric({ icon: Icon, label, value, unit }: { icon: typeof ArrowDown; label: string; value: number | null; unit: string }) {
  const rendered = value == null ? 'Not available' : new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(value)
  return <div className="border-r border-line px-1 py-5 last:border-r-0 sm:py-6"><div className="mb-4 flex items-center gap-2 text-xs font-semibold text-ink-muted"><Icon className="size-3.5" />{label}</div><div className="text-xl font-semibold tabular-nums sm:text-2xl">{rendered}{value != null && <span className="ml-1 text-xs text-ink-muted">{unit}</span>}</div></div>
}

function ProviderState({ provider }: { provider: ProviderSummary }) {
  const available = provider.healthState === 'available'
  return (
    <div className="flex min-w-0 items-start gap-3 bg-canvas px-0 py-5 sm:px-6">
      <span className={available ? 'mt-1.5 size-2 shrink-0 rounded-full bg-ok' : 'mt-1.5 size-2 shrink-0 rounded-full bg-ink-muted/40'} />
      <div className="min-w-0">
        <p className="font-semibold">{provider.displayName}</p>
        <p className="mt-1 break-words text-sm text-ink-muted">
          {available ? `Available${provider.version ? ` · CLI ${provider.version}` : ''}` : provider.message ?? 'Unavailable'}
        </p>
      </div>
    </div>
  )
}

function formatDate(value: string) { return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) }
function formatServer(name: string | null, location: string | null) { return [name, location !== name ? location : null].filter(Boolean).join(' · ') || 'Server unavailable' }
