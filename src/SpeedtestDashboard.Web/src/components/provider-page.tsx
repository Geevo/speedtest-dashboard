import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import {
  ArrowDown,
  ArrowUp,
  Check,
  CircleStop,
  ExternalLink,
  Gauge,
  LoaderCircle,
  Radio,
  Settings,
  Timer,
  Waves,
} from 'lucide-react'
import { useSpeedTestJob } from '../hooks/use-speed-test-job'
import {
  cancelSpeedTest,
  createSpeedTest,
  getProvider,
  getProviderServers,
  isTerminalJob,
  type SpeedTestJob,
  type SpeedTestServer,
} from '../lib/speed-tests'
import { cn } from '../lib/utils'
import { Button } from './ui/button'
import { ServerCombobox } from './server-combobox'

type ProviderPageProps = {
  providerId: string
  jobId: string | null
  onJobIdChange: (jobId: string) => void
  onOpenSettings: () => void
}

export function SpeedTestProviderPage({ providerId, jobId, onJobIdChange, onOpenSettings }: ProviderPageProps) {
  const [selectedServerId, setSelectedServerId] = useState<string | null>(null)
  const provider = useQuery({
    queryKey: ['provider', providerId],
    queryFn: ({ signal }) => getProvider(providerId, signal),
    refetchInterval: 60_000,
  })
  const descriptor = provider.data
  const providerName = descriptor?.displayName ?? providerId
  const operational = provider.data?.healthState !== 'unavailable'
  const supportsServerDiscovery = provider.data?.capabilities.includes('serverDiscovery') ?? false
  const servers = useQuery({
    queryKey: ['provider-servers', providerId],
    queryFn: ({ signal }) => getProviderServers(providerId, signal),
    enabled: provider.isSuccess && operational && supportsServerDiscovery,
    staleTime: 5 * 60_000,
  })
  const job = useSpeedTestJob(jobId)
  const activeJob = job.data && !isTerminalJob(job.data.status)
  const selectedServer = servers.data?.find((server) => server.id === selectedServerId)
  const createTest = useMutation({
    mutationFn: () => createSpeedTest({ providerId, serverId: selectedServerId }),
    onSuccess: (created) => onJobIdChange(created.id),
  })
  const cancelTest = useMutation({
    mutationFn: () => cancelSpeedTest(jobId!),
  })

  return (
    <div className="page-enter">
      <header className="mb-10 flex flex-col gap-3 border-b border-line pb-6 sm:flex-row sm:items-end sm:justify-between">
        <h1 className="text-[clamp(2rem,5vw,3.5rem)] font-semibold leading-none tracking-[-0.055em]">{providerName}</h1>
        <ProviderStatus provider={provider.data} loading={provider.isLoading} error={provider.isError} />
      </header>

      {provider.data?.healthState === 'unavailable' && (
        <div className="flex flex-col gap-4 border-b border-line pb-8 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <p className="text-sm font-semibold">{provider.data.message ?? 'Provider unavailable'}</p>
            <p className="mt-1 text-sm text-ink-muted">{provider.data.unavailableGuidance}</p>
          </div>
          <Button variant="outline" className="min-h-11 shrink-0" onClick={onOpenSettings}>
            <Settings className="size-4" /> Settings
          </Button>
        </div>
      )}

      {operational && descriptor && (
        <section aria-labelledby="test-options-heading">
          <h2 id="test-options-heading" className="sr-only">Test options</h2>
          {descriptor.disclosures.map((disclosure) => (
            <aside key={`${disclosure.kind}:${disclosure.url ?? disclosure.message}`} className="mb-5 border-l-2 border-signal bg-signal-soft px-4 py-3 text-sm leading-6 text-ink-muted">
              {disclosure.message}{' '}
              {disclosure.url && <a className="inline-flex items-center gap-1 font-semibold text-ink underline underline-offset-4" href={disclosure.url} target="_blank" rel="noreferrer">
                {disclosure.kind === 'privacy' ? 'Privacy details' : 'Learn more'} <ExternalLink className="size-3.5" aria-hidden="true" />
              </a>}
            </aside>
          ))}
          <div className="flex flex-col gap-5 md:flex-row md:items-end">
            {supportsServerDiscovery ? (
              <div className="min-w-0 flex-1">
                <p className="mb-2 text-sm font-semibold">Server</p>
                <ServerCombobox
                  providerName={providerName}
                  servers={servers.data ?? []}
                  selectedServerId={selectedServerId}
                  loading={servers.isLoading}
                  error={servers.isError}
                  searchPlaceholder={descriptor.serverSearchLabel}
                  onSelect={setSelectedServerId}
                />
              </div>
            ) : (
              <div className="min-w-0 flex-1">
                <p className="mb-2 text-sm font-semibold">Server</p>
                <div className="flex h-12 items-center rounded-xl border border-line bg-paper px-4 text-sm text-ink-muted">
                  Chosen automatically by {providerName}
                </div>
              </div>
            )}
            <Button
              className="h-12 shrink-0 px-7 md:min-w-44"
              disabled={Boolean(activeJob) || createTest.isPending}
              onClick={() => createTest.mutate()}
            >
              {createTest.isPending ? <LoaderCircle className="size-4 animate-spin" /> : <Gauge className="size-4" />}
              {activeJob ? 'Test running' : 'Run speed test'}
            </Button>
          </div>
          {createTest.isError && (
            <p role="alert" className="mt-3 text-sm text-danger">The test could not be started. Check the provider status and try again.</p>
          )}
        </section>
      )}

      {job.data && <JobPanel job={job.data} selectedServer={selectedServer} providerName={providerName} cancelling={cancelTest.isPending} onCancel={() => cancelTest.mutate()} />}
    </div>
  )
}

function ProviderStatus({
  provider,
  loading,
  error,
}: {
  provider: Awaited<ReturnType<typeof getProvider>> | undefined
  loading: boolean
  error: boolean
}) {
  const state = provider?.healthState
  return (
    <div className="flex items-center gap-2 text-sm text-ink-muted" role="status">
      <span className={cn('size-2 rounded-full', state === 'available' ? 'bg-ok' : 'bg-ink-muted/35', loading && 'animate-pulse')} />
      <span>
        {loading ? 'Checking provider' : error ? 'Status unavailable' : state ? state[0].toUpperCase() + state.slice(1) : 'Status unavailable'}
        {provider?.version ? ` · CLI ${provider.version}` : ''}
      </span>
    </div>
  )
}

function JobPanel({
  job,
  selectedServer,
  providerName,
  cancelling,
  onCancel,
}: {
  job: SpeedTestJob
  selectedServer: SpeedTestServer | undefined
  providerName: string
  cancelling: boolean
  onCancel: () => void
}) {
  const active = !isTerminalJob(job.status)
  return (
    <section aria-live="polite" aria-labelledby="job-heading" className="mt-8 border-t border-line pt-8">
      <div className="flex flex-col gap-5 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-4">
          <span className={cn('grid size-10 place-items-center rounded-full', active ? 'bg-signal-soft text-signal' : job.status === 'completed' ? 'bg-ok/10 text-ok' : 'bg-danger-soft text-danger')}>
            {active ? <LoaderCircle className="size-5 animate-spin" /> : job.status === 'completed' ? <Check className="size-5" /> : <CircleStop className="size-5" />}
          </span>
          <div>
            <h2 id="job-heading" className="text-xl font-semibold tracking-[-0.025em]">{job.stage}</h2>
            <p className="mt-1 text-sm text-ink-muted">
              {providerName} · {selectedServer?.name ?? 'Automatic server'}
            </p>
          </div>
        </div>
        {active && (
          <Button variant="outline" className="min-h-11" disabled={cancelling} onClick={onCancel}>
            <CircleStop className="size-4" />
            {cancelling ? 'Cancelling' : 'Cancel test'}
          </Button>
        )}
      </div>

      {job.failure && (
        <div role="alert" className="mt-6 border-t border-line pt-5">
          <p className="text-sm font-semibold">The test did not complete</p>
          <p className="mt-1 text-sm leading-6 text-ink-muted">{job.failure.message}</p>
          <code className="mt-2 block text-xs text-danger">{job.failure.code}</code>
        </div>
      )}

      {job.result && <CompletedResult job={job} providerName={providerName} />}
    </section>
  )
}

function CompletedResult({ job, providerName }: { job: SpeedTestJob; providerName: string }) {
  const result = job.result!
  const identity = job.egressIdentity?.ipv4 ?? job.egressIdentity?.ipv6
  return (
    <div className="mt-7 border-t border-line pt-7">
      <div className="grid gap-px overflow-hidden rounded-xl border border-line bg-line sm:grid-cols-2">
        <PrimaryMetric icon={ArrowDown} label="Download" value={formatMetric(result.downloadMbps)} unit="Mbps" />
        <PrimaryMetric icon={ArrowUp} label="Upload" value={formatMetric(result.uploadMbps)} unit="Mbps" />
      </div>
      <div className="mt-px grid gap-px overflow-hidden rounded-xl border border-line bg-line grid-cols-1 min-[430px]:grid-cols-3">
        <SecondaryMetric icon={Timer} label="Latency" value={formatMetric(result.latencyMilliseconds)} unit="ms" />
        <SecondaryMetric icon={Waves} label="Jitter" value={formatMetric(result.jitterMilliseconds)} unit="ms" />
        <SecondaryMetric icon={Radio} label="Packet loss" value={formatMetric(result.packetLossPercent)} unit="%" />
      </div>

      <dl className="mt-7 grid gap-5 border-t border-line pt-6 text-sm sm:grid-cols-2 lg:grid-cols-4">
        <ResultDetail label="Server" value={result.serverName ?? 'Not available'} detail={result.serverLocation} />
        <ResultDetail label="Provider" value={providerName} detail={result.serverId ? `Server ${result.serverId}` : null} />
        <ResultDetail label="Container egress" value={identity?.address ?? 'Not available'} detail={identity?.asn ?? identity?.countryName} />
        <ResultDetail label="Completed" value={job.completedAtUtc ? new Date(job.completedAtUtc).toLocaleString() : 'Not available'} />
      </dl>

      {result.resultUrl && (
        <a
          href={result.resultUrl}
          target="_blank"
          rel="noreferrer"
          className="mt-6 inline-flex min-h-11 items-center gap-2 text-sm font-semibold text-signal underline decoration-signal/35 underline-offset-4 hover:decoration-signal"
        >
          View provider result <ExternalLink className="size-4" />
        </a>
      )}
    </div>
  )
}

function PrimaryMetric({ icon: Icon, label, value, unit }: MetricProps) {
  return (
    <div className="bg-canvas p-5 sm:p-7">
      <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-[0.14em] text-ink-muted"><Icon className="size-4" />{label}</div>
      <div className="mt-6 flex items-baseline gap-2"><span className="text-[clamp(2.25rem,6vw,4rem)] font-semibold leading-none tracking-[-0.055em] tabular-nums">{value}</span>{value !== 'Not available' && <span className="text-sm font-semibold text-ink-muted">{unit}</span>}</div>
    </div>
  )
}

function SecondaryMetric({ icon: Icon, label, value, unit }: MetricProps) {
  return (
    <div className="bg-canvas p-4 sm:p-5">
      <div className="flex items-center gap-2 text-xs font-semibold text-ink-muted"><Icon className="size-3.5" />{label}</div>
      <div className="mt-5 flex items-baseline gap-1.5"><span className="text-2xl font-semibold tracking-[-0.035em] tabular-nums">{value}</span>{value !== 'Not available' && <span className="text-xs font-semibold text-ink-muted">{unit}</span>}</div>
    </div>
  )
}

type MetricProps = { icon: typeof ArrowDown; label: string; value: string; unit: string }

function ResultDetail({ label, value, detail }: { label: string; value: string; detail?: string | null }) {
  return <div><dt className="text-xs font-bold uppercase tracking-[0.12em] text-ink-muted">{label}</dt><dd className="mt-2 break-words font-semibold">{value}</dd>{detail && <dd className="mt-1 text-xs text-ink-muted">{detail}</dd>}</div>
}

function formatMetric(value: number | null) {
  return value === null ? 'Not available' : new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(value)
}
