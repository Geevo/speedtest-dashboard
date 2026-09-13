import { lazy, Suspense, useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { AppShell } from './components/app-shell'
import { OverviewPage } from './components/overview-page'
import { SpeedTestProviderPage } from './components/provider-page'
import { SchedulesPage } from './components/schedules-page'
import { SettingsPage } from './components/settings-page'
import { useTheme } from './hooks/use-theme'
import { authExpiredEvent, getSession, logout, type SessionResponse } from './lib/api'
import { LoginPage } from './components/login-page'
import { pathForNavigation, resolveNavigation } from './lib/routes'
import { getProviders } from './lib/speed-tests'
import { providerIdFromNavigation, type NavigationItem } from './lib/navigation'

const ResultsPage = lazy(() => import('./components/results-page').then((module) => ({ default: module.ResultsPage })))
const StatisticsPage = lazy(() => import('./components/statistics-page').then((module) => ({ default: module.StatisticsPage })))

export default function App() {
  const { theme, setTheme } = useTheme()
  const queryClient = useQueryClient()
  const [session, setSession] = useState<SessionResponse | null>(null)
  const [sessionFailed, setSessionFailed] = useState(false)
  const [activeItem, setActiveItem] = useState<NavigationItem>(() => resolveNavigation(window.location.pathname).item)
  const [providerJobIds, setProviderJobIds] = useState<Record<string, string | null>>({})
  const providers = useQuery({ queryKey: ['providers'], queryFn: ({ signal }) => getProviders(signal) })
  const activeProviderId = providerIdFromNavigation(activeItem)

  useEffect(() => {
    const controller = new AbortController()
    void getSession(controller.signal)
      .then(setSession)
      .catch(() => setSessionFailed(true))
    const expired = () => {
      queryClient.clear()
      setProviderJobIds({})
      setSession({ mode: 'local', authenticated: false, user: null, loginConfigured: true, showDisabledWarning: true })
    }
    window.addEventListener(authExpiredEvent, expired)
    return () => {
      controller.abort()
      window.removeEventListener(authExpiredEvent, expired)
    }
  }, [queryClient])

  useEffect(() => {
    const initial = resolveNavigation(window.location.pathname)
    if (initial.shouldRedirect) {
      window.history.replaceState(null, '', `${initial.canonicalPath}${window.location.search}${window.location.hash}`)
    }

    const onPopState = () => {
      const resolved = resolveNavigation(window.location.pathname)
      if (resolved.shouldRedirect) {
        window.history.replaceState(null, '', `${resolved.canonicalPath}${window.location.search}${window.location.hash}`)
      }
      setActiveItem(resolved.item)
    }
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [])

  useEffect(() => {
    const providerId = providerIdFromNavigation(activeItem)
    const providerName = providers.data?.find((provider) => provider.id === providerId)?.displayName
    const pageName = providerId === null
      ? activeItem[0].toUpperCase() + activeItem.slice(1)
      : providerName ?? providerId
    document.title = `${pageName} · Speedtest Dashboard`
  }, [activeItem, providers.data])

  if (session === null) {
    return (
      <div className="grid min-h-screen place-items-center bg-canvas px-6 text-center text-ink">
        <div role="status">
          <div className="mx-auto mb-4 size-5 animate-spin rounded-full border-2 border-line border-t-ink" />
          <p className="text-sm font-semibold">{sessionFailed ? 'Dashboard unavailable' : 'Opening dashboard'}</p>
          {sessionFailed && <p className="mt-2 text-sm text-ink-muted">Check the server and refresh this page.</p>}
        </div>
      </div>
    )
  }

  if (session.mode === 'local' && !session.authenticated) {
    return <LoginPage onSignedIn={setSession} />
  }

  const signOut = async () => {
    await logout()
    queryClient.clear()
    setSession({ mode: 'local', authenticated: false, user: null, loginConfigured: true, showDisabledWarning: session.showDisabledWarning })
  }

  const navigate = (item: NavigationItem) => {
    const path = pathForNavigation(item)
    if (window.location.pathname !== path) {
      window.history.pushState(null, '', path)
    }
    setActiveItem(item)
  }

  return (
    <AppShell activeItem={activeItem} onNavigate={navigate} session={session} onSignOut={signOut}>
      {activeItem === 'overview' ? (
        <OverviewPage />
      ) : activeItem === 'results' ? (
        <Suspense fallback={<div className="grid min-h-[60vh] place-items-center text-sm text-ink-muted">Loading results</div>}>
          <ResultsPage />
        </Suspense>
      ) : activeItem === 'statistics' ? (
        <Suspense fallback={<div className="grid min-h-[60vh] place-items-center text-sm text-ink-muted">Loading statistics</div>}>
          <StatisticsPage />
        </Suspense>
      ) : activeProviderId !== null ? (
        <SpeedTestProviderPage
          providerId={activeProviderId}
          jobId={providerJobIds[activeProviderId] ?? null}
          onJobIdChange={(jobId) => setProviderJobIds((current) => ({ ...current, [activeProviderId]: jobId }))}
          onOpenSettings={() => navigate('settings')}
        />
      ) : activeItem === 'schedules' ? (
        <SchedulesPage />
      ) : (
        <SettingsPage theme={theme} onThemeChange={setTheme} session={session} onSessionChange={setSession} />
      )}
    </AppShell>
  )
}
