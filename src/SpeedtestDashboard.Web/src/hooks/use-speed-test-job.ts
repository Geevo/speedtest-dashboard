import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getSpeedTestJob,
  isTerminalJob,
  speedTestJobKey,
  subscribeToSpeedTest,
  type SpeedTestJob,
} from '../lib/speed-tests'

const maximumStreamFailures = 3
const fallbackPollMilliseconds = 5_000

export function useSpeedTestJob(jobId: string | null) {
  const queryClient = useQueryClient()
  const [streamState, setStreamState] = useState({ jobId, failures: 0 })
  const streamFailures = streamState.jobId === jobId ? streamState.failures : 0
  const usingPollingFallback = streamFailures >= maximumStreamFailures
  const query = useQuery({
    queryKey: speedTestJobKey(jobId ?? 'none'),
    queryFn: ({ signal }) => getSpeedTestJob(jobId!, signal),
    enabled: jobId !== null,
    staleTime: Number.POSITIVE_INFINITY,
    refetchInterval: ({ state }) => {
      const job = state.data as SpeedTestJob | undefined
      return usingPollingFallback && job && !isTerminalJob(job.status)
        ? fallbackPollMilliseconds
        : false
    },
  })

  useEffect(() => {
    if (!jobId || usingPollingFallback || (query.data && isTerminalJob(query.data.status))) {
      return
    }

    return subscribeToSpeedTest(
      jobId,
      (event) => {
        if (!event.job) return

        queryClient.setQueryData<SpeedTestJob>(speedTestJobKey(jobId), (current) =>
          !current || event.job!.version > current.version ? event.job! : current,
        )
        if (isTerminalJob(event.job.status)) {
          void queryClient.invalidateQueries({ queryKey: ['history'] })
        }
      },
      () =>
        setStreamState((current) => ({
          jobId,
          failures: Math.min(
            maximumStreamFailures,
            (current.jobId === jobId ? current.failures : 0) + 1,
          ),
        })),
    )
  }, [jobId, query.data, queryClient, usingPollingFallback])

  useEffect(() => {
    if (query.data && isTerminalJob(query.data.status)) {
      void queryClient.invalidateQueries({ queryKey: ['history'] })
    }
  }, [query.data, queryClient])

  return { ...query, usingPollingFallback }
}
