import { useEffect, useRef, useState, type ReactNode } from 'react'
import {
  Activity,
  CalendarClock,
  ChartNoAxesCombined,
  ChevronLeft,
  Gauge,
  ListChecks,
  Menu,
  RadioTower,
  Settings,
  LogOut,
  UserRound,
  X,
} from 'lucide-react'
import { useQuery } from '@tanstack/react-query'
import { getHealth, type SessionResponse } from '../lib/api'
import { cn } from '../lib/utils'
import { Button } from './ui/button'

export type NavigationItem = 'overview' | 'ookla' | 'librespeed' | 'results' | 'statistics' | 'schedules' | 'settings'

type AppShellProps = {
  activeItem: NavigationItem
  onNavigate: (item: NavigationItem) => void
  children: ReactNode
  session: SessionResponse
  onSignOut: () => Promise<void>
}

const primaryNavigation = [
  { id: 'overview' as const, label: 'Overview', icon: Gauge },
  { id: 'results' as const, label: 'Results', icon: ListChecks },
  { id: 'statistics' as const, label: 'Statistics', icon: ChartNoAxesCombined },
  { id: 'schedules' as const, label: 'Schedules', icon: CalendarClock },
]

const providerNavigation = [
  { id: 'ookla' as const, label: 'Ookla', icon: Activity },
  { id: 'librespeed' as const, label: 'LibreSpeed', icon: RadioTower },
]

export function AppShell({ activeItem, onNavigate, children, session, onSignOut }: AppShellProps) {
  const [collapsed, setCollapsed] = useState(false)
  const [mobileOpen, setMobileOpen] = useState(false)
  const drawerRef = useRef<HTMLElement>(null)
  const menuButtonRef = useRef<HTMLButtonElement>(null)
  const health = useQuery({
    queryKey: ['health'],
    queryFn: ({ signal }) => getHealth(signal),
    refetchInterval: 30_000,
  })

  const navigate = (item: NavigationItem) => {
    onNavigate(item)
    setMobileOpen(false)
  }

  useEffect(() => {
    if (!mobileOpen) return

    const previousOverflow = document.body.style.overflow
    const returnFocus = menuButtonRef.current
    document.body.style.overflow = 'hidden'
    const focusable = () => Array.from(drawerRef.current?.querySelectorAll<HTMLElement>(
      'button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])',
    ) ?? [])
    focusable()[0]?.focus()

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setMobileOpen(false)
        return
      }
      if (event.key !== 'Tab') return
      const items = focusable()
      if (items.length === 0) return
      const first = items[0]
      const last = items.at(-1)!
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => {
      document.body.style.overflow = previousOverflow
      window.removeEventListener('keydown', onKeyDown)
      returnFocus?.focus()
    }
  }, [mobileOpen])

  const navigation = (
    <>
      <div className={cn('flex h-20 items-center gap-3 border-b border-line px-5', collapsed && 'lg:justify-center lg:px-0')}>
        <div className={cn('flex min-w-0 items-center gap-3', collapsed && 'lg:hidden')}>
          <div className="grid size-9 shrink-0 place-items-center rounded-xl bg-ink text-canvas">
            <ChartNoAxesCombined aria-hidden="true" className="size-5" strokeWidth={2.2} />
          </div>
          <div className="min-w-0">
            <div className="truncate text-[15px] font-bold tracking-[-0.02em]">Speedtest</div>
            <div className="truncate text-xs text-ink-muted">Dashboard</div>
          </div>
        </div>
        <Button
          variant="ghost"
          size="icon"
          className={cn('hidden size-9 shrink-0 lg:inline-flex', !collapsed && 'ml-auto')}
          aria-label={collapsed ? 'Expand navigation' : 'Collapse navigation'}
          onClick={() => setCollapsed((value) => !value)}
        >
          <ChevronLeft className={cn('size-4 transition-transform', collapsed && 'rotate-180')} />
        </Button>
      </div>

      <nav aria-label="Primary navigation" className="flex-1 overflow-y-auto px-3 py-5">
        <NavGroup collapsed={collapsed} items={primaryNavigation} activeItem={activeItem} onNavigate={navigate} />
        <div className={cn('mb-2 mt-7 px-3 text-[10px] font-bold uppercase tracking-[0.18em] text-ink-muted/75', collapsed && 'lg:text-center')}>
          <span className={cn(collapsed && 'lg:hidden')}>Speed tests</span>
          <span className={cn('hidden', collapsed && 'lg:inline')}>•••</span>
        </div>
        <NavGroup collapsed={collapsed} items={providerNavigation} activeItem={activeItem} onNavigate={navigate} />
      </nav>

      <div className="border-t border-line p-3">
        <NavButton
          collapsed={collapsed}
          item={{ id: 'settings', label: 'Settings', icon: Settings }}
          active={activeItem === 'settings'}
          onNavigate={navigate}
        />
        {session.mode === 'local' && session.user && (
          <div className={cn('mx-1 mt-3 flex items-center gap-2 border-t border-line pt-3', collapsed && 'lg:justify-center')}>
            <UserRound className={cn('size-4 shrink-0 text-ink-muted', collapsed && 'lg:hidden')} aria-hidden="true" />
            <span className={cn('min-w-0 flex-1 truncate text-xs font-semibold', collapsed && 'lg:hidden')}>{session.user.username}</span>
            <Button
              variant="ghost"
              size="icon"
              className="size-9"
              aria-label="Sign out"
              title="Sign out"
              onClick={() => void onSignOut()}
            >
              <LogOut className="size-4" />
            </Button>
          </div>
        )}
        <div
          className={cn('mx-2 mt-3 flex items-center gap-2 border-t border-line px-1 pt-4 text-xs text-ink-muted', collapsed && 'lg:justify-center')}
          title={health.isSuccess ? 'API online' : health.isError ? 'API unavailable' : 'Checking API'}
        >
          <span
            role="status"
            aria-label={health.isSuccess ? 'API online' : health.isError ? 'API unavailable' : 'Checking API'}
            className={cn(
              'size-2 shrink-0 rounded-full',
              health.isSuccess ? 'bg-ok' : health.isError ? 'bg-danger' : 'animate-pulse bg-ink-muted/40',
            )}
          />
          <span className={cn(collapsed && 'lg:hidden')}>
            {health.isSuccess ? `v${health.data.version}` : health.isError ? 'Offline' : 'Connecting'}
          </span>
        </div>
      </div>
    </>
  )

  return (
    <div className="min-h-screen bg-canvas text-ink">
      <aside
        className={cn(
          'fixed inset-y-0 left-0 z-30 hidden border-r border-line bg-paper transition-[width] duration-300 lg:flex lg:flex-col',
          collapsed ? 'w-[5.25rem]' : 'w-[var(--sidebar-width)]',
        )}
      >
        {navigation}
      </aside>

      {mobileOpen && (
        <div className="fixed inset-0 z-40 lg:hidden">
          <button
            aria-label="Close navigation"
            tabIndex={-1}
            className="absolute inset-0 bg-ink/35"
            onClick={() => setMobileOpen(false)}
          />
          <aside ref={drawerRef} role="dialog" aria-modal="true" aria-label="Navigation" className="absolute inset-y-0 left-0 flex w-[min(19rem,88vw)] flex-col bg-paper shadow-2xl">
            {navigation}
            <Button
              variant="ghost"
              size="icon"
              className="absolute right-3 top-5"
              aria-label="Close navigation"
              onClick={() => setMobileOpen(false)}
            >
              <X className="size-5" />
            </Button>
          </aside>
        </div>
      )}

      <div className={cn('transition-[padding] duration-300', collapsed ? 'lg:pl-[5.25rem]' : 'lg:pl-[var(--sidebar-width)]')}>
        <header className="sticky top-0 z-20 flex h-16 items-center gap-3 border-b border-line bg-canvas/95 px-4 backdrop-blur-sm lg:hidden">
          <Button ref={menuButtonRef} variant="outline" size="icon" aria-label="Open navigation" onClick={() => setMobileOpen(true)}>
            <Menu className="size-5" />
          </Button>
          <span className="text-sm font-bold">Speedtest</span>
        </header>
        {session.mode === 'none' && session.showDisabledWarning && (
          <div role="status" className="border-b border-line bg-signal-soft px-4 py-2.5 text-center text-xs font-semibold text-ink sm:px-7">
            Authentication disabled · Dashboard access is not protected by the application
          </div>
        )}
        <main className="mx-auto min-h-screen max-w-[1280px] px-4 py-8 sm:px-7 lg:px-12 lg:py-14">
          {children}
        </main>
      </div>
    </div>
  )
}

type NavItem = { id: NavigationItem; label: string; icon: typeof Gauge }

function NavGroup({
  items,
  ...props
}: {
  items: NavItem[]
  activeItem: NavigationItem
  collapsed: boolean
  onNavigate: (item: NavigationItem) => void
}) {
  return (
    <div className="space-y-1">
      {items.map((item) => (
        <NavButton key={item.id} item={item} active={props.activeItem === item.id} {...props} />
      ))}
    </div>
  )
}

function NavButton({ item, active, collapsed, onNavigate }: { item: NavItem; active: boolean; collapsed: boolean; onNavigate: (item: NavigationItem) => void }) {
  const Icon = item.icon
  return (
    <button
      type="button"
      title={collapsed ? item.label : undefined}
      className={cn(
        'group flex h-11 w-full items-center gap-3 rounded-xl px-3 text-sm font-semibold transition-colors',
        active ? 'bg-ink text-canvas' : 'text-ink-muted hover:bg-ink/5 hover:text-ink',
        collapsed && 'lg:justify-center',
      )}
      onClick={() => onNavigate(item.id)}
    >
      <Icon className="size-[18px] shrink-0" strokeWidth={active ? 2.2 : 1.8} />
      <span className={cn(collapsed && 'lg:hidden')}>{item.label}</span>
    </button>
  )
}
