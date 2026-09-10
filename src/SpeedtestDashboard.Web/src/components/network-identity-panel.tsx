import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Globe2, RefreshCw, TriangleAlert } from 'lucide-react'
import {
  ApiError,
  getNetworkIdentity,
  type NetworkAddressIdentity,
  type NetworkIdentityResponse,
} from '../lib/api'
import { formatLocation } from '../lib/network-identity'
import { cn } from '../lib/utils'
import { Button } from './ui/button'

const networkIdentityKey = ['network-identity'] as const

export function NetworkIdentityPanel() {
  const queryClient = useQueryClient()
  const [now, setNow] = useState(() => Date.now())
  const identity = useQuery({
    queryKey: networkIdentityKey,
    queryFn: ({ signal }) => getNetworkIdentity(false, signal),
    staleTime: Number.POSITIVE_INFINITY,
    retry: 1,
  })
  const refresh = useMutation({
    mutationFn: () => getNetworkIdentity(true),
    onSuccess: (data) => {
      queryClient.setQueryData(networkIdentityKey, data)
      setNow(Date.now())
    },
  })

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 30_000)
    return () => window.clearInterval(timer)
  }, [])

  if (identity.isPending) {
    return <IdentityLoading />
  }

  if (identity.isError) {
    return (
      <section aria-labelledby="identity-heading">
        <h2 id="identity-heading" className="text-2xl font-semibold tracking-[-0.035em]">Network</h2>
        <div className="mt-5 flex flex-col gap-4 border-y border-line py-6 sm:flex-row sm:items-center sm:justify-between">
          <p className="text-sm text-ink-muted">Public addresses could not be loaded.</p>
          <Button variant="outline" onClick={() => void identity.refetch()}>
            <RefreshCw className="size-4" /> Try again
          </Button>
        </div>
      </section>
    )
  }

  const data = identity.data
  const mixedEgress = hasMixedEgress(data)
  const refreshError = refresh.error instanceof ApiError ? refresh.error : null

  return (
    <section aria-labelledby="identity-heading">
      <div className="mb-5 flex items-start justify-between gap-5">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <h2 id="identity-heading" className="text-2xl font-semibold tracking-[-0.035em]">Network</h2>
            {data.state === 'partial' && <span className="rounded-full bg-ink/6 px-2.5 py-1 text-xs font-bold text-ink-muted">Partial</span>}
            {data.isStale && <span className="rounded-full bg-ink/6 px-2.5 py-1 text-xs font-bold text-ink-muted">Stale</span>}
          </div>
        </div>
          <Button
            variant="outline"
            size="sm"
            disabled={refresh.isPending}
            aria-label="Refresh network identity"
            onClick={() => refresh.mutate()}
          >
            <RefreshCw className={cn('size-4', refresh.isPending && 'animate-spin motion-reduce:animate-none')} />
            <span className="hidden sm:inline">{refresh.isPending ? 'Refreshing' : 'Refresh'}</span>
          </Button>
      </div>

      <div className="grid border-y border-line md:grid-cols-2 md:divide-x md:divide-line">
        <AddressBlock label="IPv4" identity={data.ipv4} />
        <AddressBlock label="IPv6" identity={data.ipv6} />
      </div>

      <div className="mt-3 flex flex-col gap-2 text-xs text-ink-muted sm:flex-row sm:items-center sm:justify-between">
        <p>{data.isStale ? 'Last checked' : 'Checked'} {formatRelativeTime(data.checkedAtUtc, now)}</p>
        {hasIpConfigMetadata(data) && <p>IP metadata by <a className="font-semibold text-ink underline decoration-line underline-offset-4 hover:decoration-ink" href="https://ipconfig.io" rel="noreferrer" target="_blank">ipconfig.io</a></p>}
      </div>

      {(data.isStale || refreshError) && (
        <p className="mt-3 text-sm font-semibold text-danger" role="status">
          {refreshError?.status === 429
            ? `Refresh available in about ${refreshError.retryAfterSeconds ?? 10} seconds.`
            : 'Refresh failed. Showing the last result.'}
        </p>
      )}

      {mixedEgress && (
        <div className="mt-4 flex gap-3 border-l-2 border-ink-muted bg-ink/5 px-4 py-3" role="status">
          <TriangleAlert className="mt-0.5 size-4 shrink-0 text-ink-muted" />
          <div>
            <p className="text-sm font-bold">Mixed egress</p>
            <p className="mt-1 text-sm text-ink-muted">IPv4 and IPv6 use different networks.</p>
          </div>
        </div>
      )}
    </section>
  )
}

function AddressBlock({ label, identity }: { label: string; identity: NetworkAddressIdentity | null }) {
  const location = identity ? formatLocation(identity) : null
  return (
    <article className="py-5 md:min-h-36 md:px-6 md:first:pl-0">
      <div className="mb-4 flex items-center gap-2 text-xs font-bold uppercase tracking-[0.14em] text-ink-muted">
        <Globe2 className="size-3.5" />
        {label}
      </div>
      {identity ? (
        <div>
          <p className="break-all text-lg font-semibold tracking-normal tabular-nums sm:text-xl">{identity.address}</p>
          {(identity.asn || identity.asName) && (
            <p className="mt-3 text-sm font-semibold">{[identity.asn, identity.asName].filter(Boolean).join(' · ')}</p>
          )}
          {location && <p className="mt-1 text-sm text-ink-muted">{location}</p>}
        </div>
      ) : (
        <p className="text-lg font-semibold text-ink-muted">Not available</p>
      )}
    </article>
  )
}

function IdentityLoading() {
  return (
    <section aria-labelledby="identity-heading" aria-busy="true">
      <h2 id="identity-heading" className="text-2xl font-semibold tracking-[-0.035em]">Network</h2>
      <div className="mt-5 grid border-y border-line md:grid-cols-2 md:divide-x md:divide-line">
        {[0, 1].map((item) => (
          <div key={item} className="min-h-36 py-5 md:px-6 md:first:pl-0">
            <div className="h-3 w-10 animate-pulse rounded bg-line motion-reduce:animate-none" />
            <div className="mt-6 h-6 w-4/5 animate-pulse rounded bg-line/75 motion-reduce:animate-none" />
          </div>
        ))}
      </div>
    </section>
  )
}

function hasMixedEgress(identity: NetworkIdentityResponse) {
  if (!identity.ipv4 || !identity.ipv6) return false

  const asnDiffers = Boolean(identity.ipv4.asn && identity.ipv6.asn && identity.ipv4.asn !== identity.ipv6.asn)
  const countryDiffers = Boolean(
    identity.ipv4.countryCode &&
      identity.ipv6.countryCode &&
      identity.ipv4.countryCode !== identity.ipv6.countryCode,
  )
  return asnDiffers || countryDiffers
}

function hasIpConfigMetadata(identity: NetworkIdentityResponse) {
  return [identity.ipv4, identity.ipv6].some((address) => address?.metadataSource === 'IPConfig.io')
}

function formatRelativeTime(timestamp: string, now: number) {
  const elapsedSeconds = Math.max(0, Math.floor((now - new Date(timestamp).getTime()) / 1000))
  if (elapsedSeconds < 10) return 'just now'
  if (elapsedSeconds < 60) return `${elapsedSeconds} seconds ago`
  const minutes = Math.floor(elapsedSeconds / 60)
  if (minutes < 60) return `${minutes} minute${minutes === 1 ? '' : 's'} ago`
  const hours = Math.floor(minutes / 60)
  return `${hours} hour${hours === 1 ? '' : 's'} ago`
}
