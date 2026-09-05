import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  AreaChart,
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
  Area,
} from 'recharts'
import {
  ArrowDown,
  ArrowLeft,
  ArrowRight,
  ArrowUp,
  CheckCircle2,
  ChevronDown,
  Clock3,
  ExternalLink,
  Gauge,
  History,
  LoaderCircle,
  MapPin,
  Radio,
  Trash2,
  XCircle,
} from 'lucide-react'
import {
  deleteHistory,
  getHistory,
  getHistoryDetail,
  historyQueryKey,
  type HistoryFilters,
  type HistoryListItem,
  type HistoryStatus,
} from '../lib/history'
import { cn } from '../lib/utils'
import { Button } from './ui/button'

type TimeRange = '24h' | '7d' | '30d' | 'all'

export function HistoryPage() {
  const queryClient = useQueryClient()
  const [provider, setProvider] = useState('all')
  const [status, setStatus] = useState<'all' | HistoryStatus>('all')
  const [range, setRange] = useState<TimeRange>('30d')
  const [cursor, setCursor] = useState<string | null>(null)
  const [previousCursors, setPreviousCursors] = useState<(string | null)[]>([])
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  const filters = useMemo<HistoryFilters>(() => ({
    providerId: provider === 'all' ? undefined : provider,
    status: status === 'all' ? undefined : status,
    fromUtc: rangeStart(range),
    limit: 100,
    cursor,
  }), [cursor, provider, range, status])

  const history = useQuery({
    queryKey: historyQueryKey(filters),
    queryFn: ({ signal }) => getHistory(filters, signal),
  })
  const detail = useQuery({
    queryKey: ['history-detail', selectedId],
    queryFn: ({ signal }) => getHistoryDetail(selectedId!, signal),
    enabled: selectedId !== null,
  })
  const remove = useMutation({
    mutationFn: () => deleteHistory(selectedId!),
    onSuccess: async () => {
      setSelectedId(null)
      setConfirmingDelete(false)
      await queryClient.invalidateQueries({ queryKey: ['history'] })
      await queryClient.invalidateQueries({ queryKey: ['history-detail'] })
    },
  })

  const resetPagination = () => {
    setCursor(null)
    setPreviousCursors([])
    setSelectedId(null)
  }
  const chartData = useMemo(() => [...(history.data?.items ?? [])]
    .filter((item) => item.status === 'completed')
    .reverse()
    .map((item) => ({
      label: compactTime(item.completedAtUtc),
      download: item.downloadMbps ?? undefined,
      upload: item.uploadMbps ?? undefined,
      latency: item.latencyMilliseconds ?? undefined,
      jitter: item.jitterMilliseconds ?? undefined,
    })), [history.data?.items])

  return (
    <div className="page-enter">
      <header className="mb-7 border-b border-line pb-6 lg:mb-9">
        <h1 className="text-[clamp(2rem,5vw,3.5rem)] font-semibold leading-none tracking-[-0.055em]">History</h1>
        <p className="mt-2 text-sm text-ink-muted">Latest 100 matching tests.</p>
      </header>

      <section aria-label="History filters" className="grid gap-3 border-b border-line pb-6 sm:grid-cols-3 xl:grid-cols-[13rem_13rem_1fr]">
        <FilterSelect label="Provider" value={provider} onChange={(value) => { setProvider(value); resetPagination() }}>
          <option value="all">All providers</option>
          <option value="ookla">Ookla</option>
          <option value="librespeed">LibreSpeed</option>
        </FilterSelect>
        <FilterSelect label="Status" value={status} onChange={(value) => { setStatus(value as typeof status); resetPagination() }}>
          <option value="all">All terminal states</option>
          <option value="completed">Completed</option>
          <option value="failed">Failed</option>
          <option value="cancelled">Cancelled</option>
        </FilterSelect>
        <fieldset>
          <legend className="mb-2 text-[10px] font-bold uppercase tracking-[0.15em] text-ink-muted">Time range</legend>
          <div className="grid grid-cols-4 overflow-hidden rounded-xl border border-line bg-paper">
            {(['24h', '7d', '30d', 'all'] as const).map((option) => (
              <button
                key={option}
                type="button"
                aria-pressed={range === option}
                className={cn('min-h-11 border-r border-line px-3 text-xs font-semibold last:border-r-0', range === option ? 'bg-ink text-canvas' : 'hover:bg-canvas')}
                onClick={() => { setRange(option); resetPagination() }}
              >
                {option === 'all' ? 'All' : option}
              </button>
            ))}
          </div>
        </fieldset>
      </section>

      {history.isLoading ? (
        <div className="grid min-h-72 place-items-center text-sm text-ink-muted"><LoaderCircle className="mb-3 size-5 animate-spin" />Loading history</div>
      ) : history.isError ? (
        <div role="alert" className="my-8 border-y border-line py-10 text-sm text-ink-muted">History could not be loaded.</div>
      ) : !history.data || history.data.items.length === 0 ? (
        <EmptyHistory />
      ) : (
        <>
          <div className="mt-8 grid gap-7 xl:grid-cols-2">
            <ChartPanel title="Historical throughput" note="Mbps · completed tests">
              <ResponsiveContainer width="100%" height={250}>
                <AreaChart data={chartData} accessibilityLayer margin={{ left: -8, right: 8, top: 12 }}>
                  <defs>
                    <linearGradient id="download-fill" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor="var(--color-chart-primary)" stopOpacity={0.2} />
                      <stop offset="100%" stopColor="var(--color-chart-primary)" stopOpacity={0} />
                    </linearGradient>
                  </defs>
                  <CartesianGrid vertical={false} stroke="var(--color-line)" />
                  <XAxis dataKey="label" tickLine={false} axisLine={false} minTickGap={32} tick={{ fontSize: 11, fill: 'var(--color-ink-muted)' }} />
                  <YAxis unit=" Mbps" width={68} tickLine={false} axisLine={false} tick={{ fontSize: 11, fill: 'var(--color-ink-muted)' }} />
                  <Tooltip contentStyle={tooltipStyle} />
                  <Area type="monotone" dataKey="download" name="Download" unit=" Mbps" stroke="var(--color-chart-primary)" fill="url(#download-fill)" strokeWidth={2.4} connectNulls={false} />
                  <Line type="monotone" dataKey="upload" name="Upload" unit=" Mbps" stroke="var(--color-chart-secondary)" strokeWidth={2.2} dot={false} connectNulls={false} />
                </AreaChart>
              </ResponsiveContainer>
            </ChartPanel>
            <ChartPanel title="Idle latency" note="Milliseconds · completed tests">
              <ResponsiveContainer width="100%" height={250}>
                <LineChart data={chartData} accessibilityLayer margin={{ left: -12, right: 8, top: 12 }}>
                  <CartesianGrid vertical={false} stroke="var(--color-line)" />
                  <XAxis dataKey="label" tickLine={false} axisLine={false} minTickGap={32} tick={{ fontSize: 11, fill: 'var(--color-ink-muted)' }} />
                  <YAxis unit=" ms" width={60} tickLine={false} axisLine={false} tick={{ fontSize: 11, fill: 'var(--color-ink-muted)' }} />
                  <Tooltip contentStyle={tooltipStyle} />
                  <Line type="monotone" dataKey="latency" name="Latency" unit=" ms" stroke="var(--color-chart-primary)" strokeWidth={2.4} dot={{ r: 2.5 }} connectNulls={false} />
                  <Line type="monotone" dataKey="jitter" name="Jitter" unit=" ms" stroke="var(--color-ink-muted)" strokeDasharray="4 4" dot={false} connectNulls={false} />
                </LineChart>
              </ResponsiveContainer>
            </ChartPanel>
          </div>

          <section aria-labelledby="records-heading" className="mt-8 border-t border-line pt-6">
            <div className="mb-5 flex items-end justify-between">
              <h2 id="records-heading" className="text-2xl font-semibold tracking-[-0.035em]">Results</h2>
              <span className="text-xs font-semibold text-ink-muted">{history.data?.items.length ?? 0} on this page</span>
            </div>
            <HistoryRecords
              items={history.data?.items ?? []}
              selectedId={selectedId}
              onSelect={(id) => {
                setSelectedId((current) => current === id ? null : id)
                setConfirmingDelete(false)
              }}
              detailProps={{
                detail: detail.data,
                loading: detail.isLoading,
                confirmingDelete,
                deleting: remove.isPending,
                deleteError: remove.isError,
                onConfirmDelete: () => setConfirmingDelete(true),
                onCancelDelete: () => setConfirmingDelete(false),
                onDelete: () => remove.mutate(),
              }}
            />
            <div className="mt-5 flex items-center justify-between">
              <Button variant="outline" className="min-h-11" disabled={previousCursors.length === 0} onClick={() => {
                const earlier = previousCursors.at(-1) ?? null
                setPreviousCursors((values) => values.slice(0, -1))
                setCursor(earlier)
              }}><ArrowLeft className="size-4" />Previous</Button>
              <Button variant="outline" className="min-h-11" disabled={!history.data?.nextCursor} onClick={() => {
                setPreviousCursors((values) => [...values, cursor])
                setCursor(history.data?.nextCursor ?? null)
              }}>Next<ArrowRight className="size-4" /></Button>
            </div>
          </section>
        </>
      )}

    </div>
  )
}

type HistoryDetailPanelProps = {
  detail: Awaited<ReturnType<typeof getHistoryDetail>> | undefined
  loading: boolean
  confirmingDelete: boolean
  deleting: boolean
  deleteError: boolean
  onConfirmDelete: () => void
  onCancelDelete: () => void
  onDelete: () => void
}

function HistoryRecords({ items, selectedId, onSelect, detailProps }: {
  items: HistoryListItem[]
  selectedId: number | null
  onSelect: (id: number) => void
  detailProps: HistoryDetailPanelProps
}) {
  return (
    <>
      <div className="hidden overflow-hidden rounded-xl border border-line bg-paper min-[1440px]:block">
        <div className="grid grid-cols-[9rem_6rem_minmax(10rem,1fr)_6.5rem_6.5rem_5.5rem_minmax(8rem,.7fr)_7.5rem] gap-3 border-b border-line bg-canvas px-4 py-3 text-[10px] font-bold uppercase tracking-[0.12em] text-ink-muted">
          <span>Time</span><span>Provider</span><span>Server</span><span>Download</span><span>Upload</span><span>Latency</span><span>Egress</span><span>Status</span>
        </div>
        {items.map((item) => (
          <div key={item.id} className="border-b border-line last:border-b-0">
            <button
              type="button"
              aria-expanded={selectedId === item.id}
              aria-controls={`history-detail-desktop-${item.id}`}
              className={cn('grid min-h-16 w-full grid-cols-[9rem_6rem_minmax(10rem,1fr)_6.5rem_6.5rem_5.5rem_minmax(8rem,.7fr)_7.5rem] items-center gap-3 px-4 py-3 text-left text-sm hover:bg-canvas', selectedId === item.id && 'bg-ink/5')}
              onClick={() => onSelect(item.id)}
            >
              <span className="text-xs text-ink-muted">{formatDate(item.completedAtUtc)}</span>
              <span className="font-semibold capitalize">{item.providerId}</span>
              <span className="min-w-0"><span className="block truncate font-semibold">{item.serverName ?? 'Not available'}</span>{distinctLocation(item) && <span className="block truncate text-xs text-ink-muted">{item.serverLocation}</span>}</span>
              <MetricText value={item.downloadMbps} unit="Mbps" />
              <MetricText value={item.uploadMbps} unit="Mbps" />
              <MetricText value={item.latencyMilliseconds} unit="ms" />
              <span className="truncate text-xs text-ink-muted">{item.ipv4Address ?? item.ipv6Address ?? 'Not available'}</span>
              <span className="flex items-center justify-between gap-2"><StatusLabel status={item.status} /><ChevronDown className={cn('size-4 shrink-0 text-ink-muted transition-transform', selectedId === item.id && 'rotate-180')} /></span>
            </button>
            {selectedId === item.id && <div id={`history-detail-desktop-${item.id}`}><HistoryDetailPanel {...detailProps} /></div>}
          </div>
        ))}
      </div>
      <div className="space-y-3 min-[1440px]:hidden">
        {items.map((item) => (
          <div key={item.id} className={cn('overflow-hidden rounded-xl border border-line bg-paper', selectedId === item.id && 'border-signal')}>
            <button
              type="button"
              aria-expanded={selectedId === item.id}
              aria-controls={`history-detail-mobile-${item.id}`}
              className={cn('min-h-11 w-full p-4 text-left', selectedId === item.id && 'bg-ink/5')}
              onClick={() => onSelect(item.id)}
            >
              <div className="flex items-start justify-between gap-4"><div><span className="text-xs text-ink-muted">{formatDate(item.completedAtUtc)}</span><p className="mt-1 font-semibold">{item.serverName ?? `${item.providerId} test`}</p>{distinctLocation(item) && <p className="mt-1 flex items-center gap-1 text-xs text-ink-muted"><MapPin className="size-3" />{item.serverLocation}</p>}</div><span className="flex items-center gap-2"><StatusLabel status={item.status} /><ChevronDown className={cn('size-4 shrink-0 text-ink-muted transition-transform', selectedId === item.id && 'rotate-180')} /></span></div>
              <div className="mt-4 grid grid-cols-3 gap-3 border-t border-line pt-3"><MiniMetric label="Down" value={item.downloadMbps} unit="Mbps" /><MiniMetric label="Up" value={item.uploadMbps} unit="Mbps" /><MiniMetric label="Latency" value={item.latencyMilliseconds} unit="ms" /></div>
            </button>
            {selectedId === item.id && <div id={`history-detail-mobile-${item.id}`}><HistoryDetailPanel {...detailProps} /></div>}
          </div>
        ))}
      </div>
    </>
  )
}

function HistoryDetailPanel({ detail, loading, confirmingDelete, deleting, deleteError, onConfirmDelete, onCancelDelete, onDelete }: HistoryDetailPanelProps) {
  const identity = detail?.egressIdentity
  return (
    <section aria-label="Test details" className="border-t border-line bg-canvas px-4 py-6 sm:px-6">
      {loading && <div className="flex min-h-24 items-center justify-center gap-2 text-sm text-ink-muted"><LoaderCircle className="size-4 animate-spin" />Loading details</div>}
      {detail && <>
        <dl className="grid gap-x-7 gap-y-5 sm:grid-cols-2 lg:grid-cols-4">
          <Detail label="Provider" value={detail.providerId} />
          <Detail label="Status" value={detail.status} />
          <Detail label="Queued" value={formatDate(detail.queuedAtUtc)} />
          <Detail label="Completed" value={formatDate(detail.completedAtUtc)} />
          <Detail label="Requested server" value={detail.requestedServerId ?? 'Automatic'} />
          <Detail label="IPv4 egress" value={identity?.ipv4?.address ?? 'Not available'} secondary={identity?.ipv4?.asName ?? identity?.ipv4?.countryName} />
          <Detail label="IPv6 egress" value={identity?.ipv6?.address ?? 'Not available'} secondary={identity?.ipv6?.asName ?? identity?.ipv6?.countryName} />
          <Detail label="Job ID" value={detail.jobId} />
        </dl>
        {detail.result && <div className="mt-6 grid grid-cols-2 gap-px overflow-hidden rounded-xl border border-line bg-line sm:grid-cols-5"><DetailMetric icon={ArrowDown} label="Download" value={detail.result.downloadMbps} unit="Mbps" /><DetailMetric icon={ArrowUp} label="Upload" value={detail.result.uploadMbps} unit="Mbps" /><DetailMetric icon={Clock3} label="Latency" value={detail.result.latencyMilliseconds} unit="ms" /><DetailMetric icon={Radio} label="Jitter" value={detail.result.jitterMilliseconds} unit="ms" /><DetailMetric icon={Gauge} label="Loss" value={detail.result.packetLossPercent} unit="%" /></div>}
        {detail.failure && <div className="mt-6 text-sm"><p className="font-semibold">{detail.failure.message}</p><code className="mt-1 block text-xs text-danger">{detail.failure.code}</code></div>}
        <div className="mt-7 flex flex-col gap-3 border-t border-line pt-6 sm:flex-row sm:items-center sm:justify-between">
          {detail.result?.resultUrl ? <a href={detail.result.resultUrl} target="_blank" rel="noreferrer" className="inline-flex min-h-11 items-center gap-2 text-sm font-semibold text-signal underline decoration-signal/35 underline-offset-4">View verified result <ExternalLink className="size-4" /></a> : <span className="text-sm text-ink-muted">No external result link</span>}
          {confirmingDelete ? <div role="group" aria-label="Confirm history deletion" className="flex flex-wrap items-center gap-2"><span className="mr-1 text-sm text-ink-muted">Delete this record permanently?</span><Button className="min-h-11 bg-danger text-white hover:bg-danger/90" disabled={deleting} onClick={onDelete}>{deleting ? <LoaderCircle className="size-4 animate-spin" /> : <Trash2 className="size-4" />}Delete</Button><Button variant="ghost" className="min-h-11" onClick={onCancelDelete}>Keep</Button></div> : <Button variant="ghost" className="min-h-11" onClick={onConfirmDelete}><Trash2 className="size-4" />Delete record</Button>}
        </div>
        {deleteError && <p role="alert" className="mt-3 text-sm text-danger">The record could not be deleted.</p>}
      </>}
    </section>
  )
}

function EmptyHistory() { return <section className="my-8 grid min-h-72 place-items-center border-y border-line py-12 text-center"><div><History className="mx-auto size-6 text-signal" /><h2 className="mt-4 text-2xl font-semibold tracking-[-0.035em]">No matching tests</h2><p className="mt-2 text-sm text-ink-muted">Change the filters or run a speed test.</p></div></section> }
function FilterSelect({ label, value, onChange, children }: { label: string; value: string; onChange: (value: string) => void; children: React.ReactNode }) { return <label><span className="mb-2 block text-[10px] font-bold uppercase tracking-[0.15em] text-ink-muted">{label}</span><select value={value} className="min-h-11 w-full rounded-xl border border-line bg-paper px-3 text-sm font-semibold outline-none focus:border-signal" onChange={(event) => onChange(event.target.value)}>{children}</select></label> }
function ChartPanel({ title, note, children }: { title: string; note: string; children: React.ReactNode }) { return <section className="min-w-0 rounded-[1.25rem] border border-line bg-paper p-4 sm:p-6"><div className="mb-2"><h2 className="text-xl font-semibold tracking-[-0.03em]">{title}</h2><p className="mt-1 text-xs text-ink-muted">{note}</p></div>{children}</section> }
function StatusLabel({ status }: { status: HistoryStatus }) { const Icon = status === 'completed' ? CheckCircle2 : XCircle; return <span className={cn('inline-flex items-center gap-1.5 rounded-full px-2 py-1 text-[11px] font-bold capitalize', status === 'completed' ? 'bg-ok/10 text-ok' : status === 'cancelled' ? 'bg-ink/6 text-ink-muted' : 'bg-danger-soft text-danger')}><Icon className="size-3" />{status}</span> }
function MetricText({ value, unit }: { value: number | null; unit: string }) { return <span className="tabular-nums">{value === null ? <span className="text-ink-muted">Not available</span> : <>{formatMetric(value)} <span className="text-xs text-ink-muted">{unit}</span></>}</span> }
function MiniMetric({ label, value, unit }: { label: string; value: number | null; unit: string }) { return <div><p className="text-[10px] font-bold uppercase tracking-[.1em] text-ink-muted">{label}</p><p className="mt-1 text-sm font-semibold tabular-nums">{value === null ? 'N/A' : `${formatMetric(value)} ${unit}`}</p></div> }
function DetailMetric({ icon: Icon, label, value, unit }: { icon: typeof Gauge; label: string; value: number | null; unit: string }) { return <div className="bg-paper p-4"><p className="flex items-center gap-1.5 text-xs text-ink-muted"><Icon className="size-3" />{label}</p><p className="mt-4 font-semibold tabular-nums">{value === null ? 'Not available' : `${formatMetric(value)} ${unit}`}</p></div> }
function Detail({ label, value, secondary }: { label: string; value: string; secondary?: string | null }) { return <div className="min-w-0"><dt className="text-[10px] font-bold uppercase tracking-[0.13em] text-ink-muted">{label}</dt><dd className="mt-2 break-words text-sm font-semibold capitalize">{value}</dd>{secondary && <dd className="mt-1 text-xs text-ink-muted">{secondary}</dd>}</div> }
function rangeStart(range: TimeRange) { if (range === 'all') return undefined; const hours = range === '24h' ? 24 : range === '7d' ? 24 * 7 : 24 * 30; return new Date(Date.now() - hours * 60 * 60 * 1000).toISOString() }
function compactTime(value: string) { return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', hour: '2-digit' }).format(new Date(value)) }
function formatDate(value: string) { return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) }
function formatMetric(value: number) { return new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(value) }
function distinctLocation(item: HistoryListItem) { return Boolean(item.serverLocation && item.serverLocation !== item.serverName) }
const tooltipStyle = { border: '1px solid var(--color-line)', borderRadius: '12px', background: 'var(--color-paper)', color: 'var(--color-ink)', fontSize: '12px' }
