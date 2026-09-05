import { useEffect, useId, useMemo, useRef, useState } from 'react'
import { Check, ChevronDown, LoaderCircle, Search } from 'lucide-react'
import type { SpeedTestServer } from '../lib/speed-tests'
import { cn } from '../lib/utils'

type ServerComboboxProps = {
  providerName: string
  servers: SpeedTestServer[]
  selectedServerId: string | null
  loading: boolean
  error: boolean
  searchPlaceholder: string
  onSelect: (serverId: string | null) => void
}

export function ServerCombobox({
  providerName,
  servers,
  selectedServerId,
  loading,
  error,
  searchPlaceholder,
  onSelect,
}: ServerComboboxProps) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const rootRef = useRef<HTMLDivElement>(null)
  const searchRef = useRef<HTMLInputElement>(null)
  const listboxId = useId()
  const selectedServer = servers.find((server) => server.id === selectedServerId)
  const filteredServers = useMemo(() => {
    const query = search.trim().toLocaleLowerCase()
    if (!query) return servers

    return servers.filter((server) =>
      [server.id, server.name, server.sponsor, server.location, server.countryCode, server.host]
        .filter(Boolean)
        .some((value) => value!.toLocaleLowerCase().includes(query)),
    )
  }, [search, servers])

  useEffect(() => {
    if (!open) return

    const closeOnOutsideClick = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', closeOnOutsideClick)
    return () => document.removeEventListener('pointerdown', closeOnOutsideClick)
  }, [open])

  const openList = () => {
    setOpen(true)
    window.requestAnimationFrame(() => searchRef.current?.focus())
  }

  const select = (serverId: string | null) => {
    onSelect(serverId)
    setSearch('')
    setOpen(false)
  }

  return (
    <div ref={rootRef} className="relative">
      <button
        type="button"
        role="combobox"
        aria-label="Server"
        aria-expanded={open}
        aria-controls={listboxId}
        aria-haspopup="listbox"
        className="flex min-h-12 w-full items-center justify-between gap-4 rounded-lg border border-line bg-paper px-4 text-left transition-colors hover:border-ink/30 focus-visible:border-signal"
        onClick={() => (open ? setOpen(false) : openList())}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown') {
            event.preventDefault()
            openList()
          }
        }}
      >
        <span className="min-w-0">
          <span className="block truncate text-sm font-semibold">{selectedServer?.name ?? 'Automatic'}</span>
          <span className="block truncate text-xs text-ink-muted">
            {selectedServer ? selectedServer.location ?? `Server ${selectedServer.id}` : `${providerName} selects the server`}
          </span>
        </span>
        <ChevronDown className={cn('size-4 shrink-0 text-ink-muted transition-transform', open && 'rotate-180')} />
      </button>

      {open && (
        <div className="absolute inset-x-0 top-[calc(100%+0.5rem)] z-30 overflow-hidden rounded-lg border border-line bg-paper shadow-xl shadow-ink/8">
          <div className="relative border-b border-line p-3">
            <Search className="pointer-events-none absolute left-6 top-1/2 size-4 -translate-y-1/2 text-ink-muted" />
            <input
              ref={searchRef}
              type="search"
              value={search}
              aria-label="Search servers"
              className="h-11 w-full rounded-md border border-line bg-canvas pl-10 pr-3 text-base outline-none focus:border-signal sm:text-sm"
              placeholder={searchPlaceholder}
              onChange={(event) => setSearch(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Escape') {
                  event.preventDefault()
                  setOpen(false)
                }
              }}
            />
          </div>

          <div id={listboxId} role="listbox" aria-label="Speed test servers" className="max-h-72 overflow-y-auto p-1.5">
            <ServerOption
              selected={selectedServerId === null}
              name="Automatic"
              detail={`${providerName} selects the server`}
              onSelect={() => select(null)}
            />
            {loading ? (
              <div className="flex min-h-20 items-center justify-center gap-2 text-sm text-ink-muted">
                <LoaderCircle className="size-4 animate-spin motion-reduce:animate-none" /> Loading servers
              </div>
            ) : error ? (
              <p className="px-3 py-5 text-sm text-ink-muted">Servers could not be loaded. Automatic selection is still available.</p>
            ) : filteredServers.length ? (
              filteredServers.map((server) => (
                <ServerOption
                  key={server.id}
                  selected={selectedServerId === server.id}
                  name={server.name}
                  detail={[server.location, `ID ${server.id}`].filter(Boolean).join(' · ')}
                  onSelect={() => select(server.id)}
                />
              ))
            ) : (
              <p className="px-3 py-5 text-sm text-ink-muted">No matching servers.</p>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

function ServerOption({ selected, name, detail, onSelect }: { selected: boolean; name: string; detail: string; onSelect: () => void }) {
  return (
    <button
      type="button"
      role="option"
      aria-selected={selected}
      className={cn(
        'flex min-h-12 w-full items-center gap-3 rounded-md px-3 py-2 text-left hover:bg-canvas focus-visible:bg-canvas',
        selected && 'bg-signal-soft/55',
      )}
      onClick={onSelect}
    >
      <span className="min-w-0 flex-1">
        <span className="block truncate text-sm font-semibold">{name}</span>
        <span className="block truncate text-xs text-ink-muted">{detail}</span>
      </span>
      {selected && <Check className="size-4 shrink-0 text-ok" />}
    </button>
  )
}
