import { useMemo, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  Area,
  AreaChart,
  CartesianGrid,
  Line,
  LineChart,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { ArrowDown, ArrowUp, Gauge, LoaderCircle, Radio, Waves } from 'lucide-react'
import {
  getStatistics,
  statisticsQueryKey,
  type MetricStatistics,
  type StatisticsProvider,
  type StatisticsRange,
} from '../lib/statistics'
import { cn } from '../lib/utils'

const ranges: { value: StatisticsRange; label: string }[] = [
  { value: '24h', label: '24h' },
  { value: '7d', label: '7 days' },
  { value: '30d', label: '30 days' },
  { value: '90d', label: '90 days' },
  { value: 'all', label: 'All time' },
]

export function StatisticsPage() {
  const [range, setRange] = useState<StatisticsRange>('7d')
  const [provider, setProvider] = useState<'all' | StatisticsProvider>('all')
  const statistics = useQuery({
    queryKey: statisticsQueryKey(range, provider === 'all' ? undefined : provider),
    queryFn: ({ signal }) => getStatistics(range, provider === 'all' ? undefined : provider, signal),
  })
  const data = statistics.data
  const chart = useMemo(() => (data?.chart ?? []).map((point) => ({
    label: chartLabel(point.bucketStartUtc, range),
    download: point.downloadMbps ?? undefined,
    upload: point.uploadMbps ?? undefined,
    latency: point.latencyMilliseconds ?? undefined,
    jitter: point.jitterMilliseconds ?? undefined,
  })), [data?.chart, range])

  return (
    <div className="page-enter">
      <header className="mb-7 border-b border-line pb-6 lg:mb-9">
        <p className="mb-2 text-[10px] font-bold uppercase tracking-[0.18em] text-signal">Persisted history</p>
        <h1 className="text-[clamp(2rem,5vw,3.5rem)] font-semibold leading-none tracking-[-0.055em]">Statistics</h1>
        <p className="mt-2 max-w-2xl text-sm text-ink-muted">A practical view of speed, responsiveness, and test reliability.</p>
      </header>

      <section aria-label="Statistics filters" className="grid gap-5 border-b border-line pb-7 lg:grid-cols-[1fr_20rem] lg:items-end">
        <fieldset>
          <legend className="mb-2 text-[10px] font-bold uppercase tracking-[0.15em] text-ink-muted">Time range</legend>
          <div className="grid grid-cols-5 overflow-hidden rounded-xl border border-line bg-paper">
            {ranges.map((option) => (
              <button
                key={option.value}
                type="button"
                aria-pressed={range === option.value}
                className={cn('min-h-11 border-r border-line px-2 text-xs font-semibold last:border-r-0 sm:px-3', range === option.value ? 'bg-ink text-canvas' : 'hover:bg-canvas')}
                onClick={() => setRange(option.value)}
              >
                <span className="sm:hidden">{option.value === 'all' ? 'All' : option.value}</span>
                <span className="hidden sm:inline">{option.label}</span>
              </button>
            ))}
          </div>
        </fieldset>
        <label>
          <span className="mb-2 block text-[10px] font-bold uppercase tracking-[0.15em] text-ink-muted">Provider</span>
          <select value={provider} className="min-h-11 w-full rounded-xl border border-line bg-paper px-3 text-sm font-semibold outline-none focus:border-signal" onChange={(event) => setProvider(event.target.value as typeof provider)}>
            <option value="all">All providers</option>
            <option value="ookla">Ookla</option>
            <option value="librespeed">LibreSpeed</option>
          </select>
        </label>
      </section>

      {statistics.isLoading ? (
        <div className="grid min-h-72 place-items-center text-sm text-ink-muted"><span className="flex items-center gap-2"><LoaderCircle className="size-4 animate-spin" />Calculating statistics</span></div>
      ) : statistics.isError || !data ? (
        <div role="alert" className="my-8 border-y border-line py-10 text-sm text-ink-muted">Statistics could not be loaded.</div>
      ) : (
        <>
          <section aria-labelledby="test-summary-heading" className="mt-9 overflow-hidden rounded-[1.25rem] border border-line bg-paper">
            <div className="grid gap-7 p-5 sm:p-7 lg:grid-cols-[1.1fr_2fr] lg:items-end">
              <div>
                <p id="test-summary-heading" className="text-[10px] font-bold uppercase tracking-[0.16em] text-ink-muted">Tests</p>
                <p className="mt-3 text-5xl font-semibold tracking-[-0.055em] tabular-nums">{data.tests.total}</p>
                <p className="mt-2 text-sm text-ink-muted">terminal tests in this window</p>
              </div>
              <dl className="grid grid-cols-2 gap-x-5 gap-y-5 sm:grid-cols-4">
                <Count label="Successful" value={data.tests.completed} tone="ok" />
                <Count label="Failed" value={data.tests.failed} tone="danger" />
                <Count label="Cancelled" value={data.tests.cancelled} />
                <Count label="Success rate" value={data.tests.successRate} suffix="%" />
              </dl>
            </div>
            <div className="h-1 bg-line"><div className="h-full bg-ok transition-[width]" style={{ width: `${data.tests.successRate ?? 0}%` }} /></div>
          </section>

          {data.tests.total === 0 ? (
            <section className="my-8 grid min-h-64 place-items-center border-y border-line py-12 text-center">
              <div><Gauge className="mx-auto size-6 text-signal" /><h2 className="mt-4 text-2xl font-semibold tracking-[-0.035em]">No tests in this range</h2><p className="mt-2 text-sm text-ink-muted">Choose a wider window or run a speed test.</p></div>
            </section>
          ) : (
            <>
              <div className="mt-7 grid gap-5 lg:grid-cols-2">
                <MetricPanel title="Download" icon={ArrowDown} metric={data.download} unit="Mbps" kind="throughput" />
                <MetricPanel title="Upload" icon={ArrowUp} metric={data.upload} unit="Mbps" kind="throughput" />
                <MetricPanel title="Latency" icon={Waves} metric={data.latency} unit="ms" kind="latency" lowerBetter />
                <MetricPanel title="Jitter" icon={Radio} metric={data.jitter} unit="ms" kind="latency" lowerBetter />
              </div>

              <section className="mt-5 flex flex-wrap items-center justify-between gap-4 rounded-xl border border-line bg-paper px-5 py-4">
                <div><p className="text-sm font-semibold">Packet loss</p><p className="mt-1 text-xs text-ink-muted">Only tests where the provider reported a measurement</p></div>
                <div className="text-right"><p className="text-2xl font-semibold tabular-nums">{metricValue(data.packetLoss?.median, '%')}</p><p className="text-xs text-ink-muted">median · {data.packetLoss?.count ?? 0} measured</p></div>
              </section>

              <div className="mt-8 grid gap-7 xl:grid-cols-2">
                <ChartPanel title="Throughput over time" note={`${data.chart.length} server-side buckets`}>
                  <ResponsiveContainer width="100%" height={280}>
                    <AreaChart data={chart} accessibilityLayer margin={{ left: -8, right: 8, top: 12 }}>
                      <defs><linearGradient id="statistics-download-fill" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stopColor="var(--color-chart-primary)" stopOpacity={0.2} /><stop offset="100%" stopColor="var(--color-chart-primary)" stopOpacity={0} /></linearGradient></defs>
                      <CartesianGrid vertical={false} stroke="var(--color-line)" />
                      <XAxis dataKey="label" tickLine={false} axisLine={false} minTickGap={32} tick={axisTick} />
                      <YAxis unit=" Mbps" width={68} tickLine={false} axisLine={false} tick={axisTick} />
                      <Tooltip contentStyle={tooltipStyle} />
                      {data.download?.median != null && <ReferenceLine y={data.download.median} stroke="var(--color-chart-primary)" strokeDasharray="5 5" strokeOpacity={0.65} />}
                      <Area type="monotone" dataKey="download" name="Download" unit=" Mbps" stroke="var(--color-chart-primary)" fill="url(#statistics-download-fill)" strokeWidth={2.4} connectNulls={false} />
                      <Line type="monotone" dataKey="upload" name="Upload" unit=" Mbps" stroke="var(--color-chart-secondary)" strokeWidth={2.2} dot={false} connectNulls={false} />
                    </AreaChart>
                  </ResponsiveContainer>
                </ChartPanel>
                <ChartPanel title="Latency and jitter" note="Milliseconds · lower is better">
                  <ResponsiveContainer width="100%" height={280}>
                    <LineChart data={chart} accessibilityLayer margin={{ left: -12, right: 8, top: 12 }}>
                      <CartesianGrid vertical={false} stroke="var(--color-line)" />
                      <XAxis dataKey="label" tickLine={false} axisLine={false} minTickGap={32} tick={axisTick} />
                      <YAxis unit=" ms" width={60} tickLine={false} axisLine={false} tick={axisTick} />
                      <Tooltip contentStyle={tooltipStyle} />
                      {data.latency?.median != null && <ReferenceLine y={data.latency.median} stroke="var(--color-chart-primary)" strokeDasharray="5 5" strokeOpacity={0.65} />}
                      <Line type="monotone" dataKey="latency" name="Latency" unit=" ms" stroke="var(--color-chart-primary)" strokeWidth={2.4} dot={false} connectNulls={false} />
                      <Line type="monotone" dataKey="jitter" name="Jitter" unit=" ms" stroke="var(--color-ink-muted)" strokeDasharray="4 4" dot={false} connectNulls={false} />
                    </LineChart>
                  </ResponsiveContainer>
                </ChartPanel>
              </div>

              {provider === 'all' && <ProviderComparison providers={data.providers} />}
            </>
          )}
        </>
      )}
    </div>
  )
}

function MetricPanel({ title, icon: Icon, metric, unit, kind, lowerBetter = false }: { title: string; icon: typeof Gauge; metric: MetricStatistics | null; unit: string; kind: 'throughput' | 'latency'; lowerBetter?: boolean }) {
  const columns = kind === 'throughput'
    ? [{ label: 'Latest', value: metric?.latest }, { label: 'Median', value: metric?.median }, { label: 'Average', value: metric?.average }, { label: 'Best', value: metric?.maximum }, { label: 'Worst', value: metric?.minimum }]
    : [{ label: 'Latest', value: metric?.latest }, { label: 'Median', value: metric?.median }, { label: 'Average', value: metric?.average }, { label: 'P95', value: metric?.p95 }, { label: 'Best', value: metric?.minimum }, { label: 'Worst', value: metric?.maximum }]
  return <section className="rounded-[1.25rem] border border-line bg-paper p-5 sm:p-6"><div className="flex items-start justify-between gap-4"><div><h2 className="flex items-center gap-2 text-xl font-semibold tracking-[-0.03em]"><Icon className="size-4 text-signal" />{title}</h2><p className="mt-1 text-xs text-ink-muted">{metric?.count ?? 0} measured</p></div><Trend value={metric?.trendPercent} lowerBetter={lowerBetter} /></div>{metric ? <dl className="mt-6 grid grid-cols-2 gap-x-5 gap-y-5 sm:grid-cols-3">{columns.map((item) => <div key={item.label}><dt className="text-[10px] font-bold uppercase tracking-[0.12em] text-ink-muted">{item.label}</dt><dd className="mt-1.5 text-lg font-semibold tabular-nums">{metricValue(item.value, unit)}</dd></div>)}</dl> : <p className="mt-7 border-t border-line pt-5 text-sm text-ink-muted">No measurements available.</p>}</section>
}

function Trend({ value, lowerBetter }: { value: number | null | undefined; lowerBetter: boolean }) {
  if (value == null) return <span className="text-xs text-ink-muted">No prior trend</span>
  const improvement = lowerBetter ? value < 0 : value > 0
  return <span className={cn('rounded-full px-2.5 py-1 text-xs font-bold tabular-nums', improvement ? 'bg-ok/10 text-ok' : value === 0 ? 'bg-ink/5 text-ink-muted' : 'bg-danger-soft text-danger')}>{value > 0 ? '+' : ''}{formatNumber(value)}% vs prior</span>
}

function ProviderComparison({ providers }: { providers: Awaited<ReturnType<typeof getStatistics>>['providers'] }) {
  return <section aria-labelledby="provider-comparison-heading" className="mt-10 border-t border-line pt-7"><div className="mb-5"><h2 id="provider-comparison-heading" className="text-2xl font-semibold tracking-[-0.035em]">Provider comparison</h2><p className="mt-1 text-sm text-ink-muted">Medians stay provider-specific because measurement support can differ.</p></div><div className="grid gap-4 lg:grid-cols-2">{providers.map((item) => <article key={item.provider} className="rounded-xl border border-line bg-paper p-5"><div className="flex items-center justify-between gap-3"><h3 className="text-lg font-semibold capitalize">{item.provider}</h3><span className="text-xs font-semibold text-ink-muted">{item.tests.completed} successful · {metricValue(item.tests.successRate, '%')}</span></div><dl className="mt-5 grid grid-cols-2 gap-5 sm:grid-cols-4"><ComparisonMetric label="Download" value={item.medianDownloadMbps} unit="Mbps" /><ComparisonMetric label="Upload" value={item.medianUploadMbps} unit="Mbps" /><ComparisonMetric label="Latency" value={item.medianLatencyMilliseconds} unit="ms" /><ComparisonMetric label="Jitter" value={item.medianJitterMilliseconds} unit="ms" /></dl></article>)}</div></section>
}

function ComparisonMetric({ label, value, unit }: { label: string; value: number | null; unit: string }) { return <div><dt className="text-[10px] font-bold uppercase tracking-[.12em] text-ink-muted">{label}</dt><dd className="mt-1.5 font-semibold tabular-nums">{metricValue(value, unit)}</dd></div> }
function Count({ label, value, suffix = '', tone }: { label: string; value: number | null; suffix?: string; tone?: 'ok' | 'danger' }) { return <div><dt className="text-[10px] font-bold uppercase tracking-[.12em] text-ink-muted">{label}</dt><dd className={cn('mt-1.5 text-xl font-semibold tabular-nums', tone === 'ok' && 'text-ok', tone === 'danger' && 'text-danger')}>{value == null ? 'N/A' : `${formatNumber(value)}${suffix}`}</dd></div> }
function ChartPanel({ title, note, children }: { title: string; note: string; children: ReactNode }) { return <section className="min-w-0 rounded-[1.25rem] border border-line bg-paper p-4 sm:p-6"><div className="mb-2"><h2 className="text-xl font-semibold tracking-[-0.03em]">{title}</h2><p className="mt-1 text-xs text-ink-muted">{note}</p></div>{children}</section> }
function metricValue(value: number | null | undefined, unit: string) { return value == null ? 'N/A' : `${formatNumber(value)} ${unit}` }
function formatNumber(value: number) { return new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(value) }
function chartLabel(value: string, range: StatisticsRange) { return new Intl.DateTimeFormat(undefined, range === '24h' ? { hour: '2-digit', minute: '2-digit' } : { month: 'short', day: 'numeric' }).format(new Date(value)) }
const tooltipStyle = { border: '1px solid var(--color-line)', borderRadius: '12px', background: 'var(--color-paper)', color: 'var(--color-ink)', fontSize: '12px' }
const axisTick = { fontSize: 11, fill: 'var(--color-ink-muted)' }
