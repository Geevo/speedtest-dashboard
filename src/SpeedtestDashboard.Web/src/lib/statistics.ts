import { apiFetch } from './api'

export type StatisticsRange = '24h' | '7d' | '30d' | '90d' | 'all'
export type StatisticsProvider = string

export type TestCountStatistics = {
  total: number
  completed: number
  failed: number
  cancelled: number
  successRate: number | null
}

export type MetricStatistics = {
  count: number
  latest: number | null
  average: number | null
  median: number | null
  minimum: number | null
  maximum: number | null
  p95: number | null
  trendPercent: number | null
}

export type StatisticsChartPoint = {
  bucketStartUtc: string
  downloadMbps: number | null
  uploadMbps: number | null
  latencyMilliseconds: number | null
  jitterMilliseconds: number | null
}

export type ProviderStatisticsComparison = {
  provider: StatisticsProvider
  tests: TestCountStatistics
  medianDownloadMbps: number | null
  medianUploadMbps: number | null
  medianLatencyMilliseconds: number | null
  medianJitterMilliseconds: number | null
  medianPacketLossPercent: number | null
}

export type SpeedTestStatistics = {
  range: StatisticsRange
  provider: StatisticsProvider | null
  fromUtc: string | null
  toUtc: string
  tests: TestCountStatistics
  download: MetricStatistics | null
  upload: MetricStatistics | null
  latency: MetricStatistics | null
  jitter: MetricStatistics | null
  packetLoss: MetricStatistics | null
  chart: StatisticsChartPoint[]
  providers: ProviderStatisticsComparison[]
}

export function statisticsQueryKey(range: StatisticsRange, provider?: StatisticsProvider) {
  return ['statistics', range, provider ?? 'all'] as const
}

export async function getStatistics(
  range: StatisticsRange,
  provider?: StatisticsProvider,
  signal?: AbortSignal,
): Promise<SpeedTestStatistics> {
  const search = new URLSearchParams({ range })
  if (provider) search.set('provider', provider)
  const response = await apiFetch(`/api/statistics?${search}`, {
    headers: { Accept: 'application/json' },
    signal,
  })
  if (!response.ok) throw new Error('Statistics could not be loaded.')
  return response.json() as Promise<SpeedTestStatistics>
}
