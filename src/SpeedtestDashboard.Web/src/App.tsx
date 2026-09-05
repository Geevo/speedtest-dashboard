import { lazy, Suspense, useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { AppShell, type NavigationItem } from './components/app-shell'
import { OverviewPage } from './components/overview-page'
import { LibreSpeedPage, OoklaPage } from './components/ookla-page'
import { SettingsPage } from './components/settings-page'
import { useTheme } from './hooks/use-theme'
import { authExpiredEvent, getSession, logout, type SessionResponse } from './lib/api'
import { LoginPage } from './components/login-page'

const HistoryPage = lazy(() => import('./components/history-page').then((module) => ({ default: module.HistoryPage })))

export default function App() {
  const { theme, setTheme } = useTheme()
  const queryClient = useQueryClient()
  const [session, setSession] = useState<SessionResponse | null>(null)
  const [sessionFailed, setSessionFailed] = useState(false)
  const [activeItem, setActiveItem] = useState<NavigationItem>('overview')
  const [ooklaJobId, setOoklaJobId] = useState<string | null>(null)
  const [libreSpeedJobId, setLibreSpeedJobId] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    void getSession(controller.signal)
      .then(setSession)
      .catch(() => setSessionFailed(true))
    const expired = () => {
      queryClient.clear()
      setOoklaJobId(null)
      setLibreSpeedJobId(null)
      setSession({ mode: 'local', authenticated: false, user: null, loginConfigured: true, showDisabledWarning: true })
    }
    window.addEventListener(authExpiredEvent, expired)
    return () => {
      controller.abort()
      window.removeEventListener(authExpiredEvent, expired)
    }
  }, [queryClient])

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

  return (
    <AppShell activeItem={activeItem} onNavigate={setActiveItem} session={session} onSignOut={signOut}>
      {activeItem === 'overview' ? (
        <OverviewPage />
      ) : activeItem === 'history' ? (
        <Suspense fallback={<div className="grid min-h-[60vh] place-items-center text-sm text-ink-muted">Loading history</div>}>
          <HistoryPage />
        </Suspense>
      ) : activeItem === 'ookla' ? (
        <OoklaPage
          jobId={ooklaJobId}
          onJobIdChange={setOoklaJobId}
          onOpenSettings={() => setActiveItem('settings')}
        />
      ) : activeItem === 'librespeed' ? (
        <LibreSpeedPage
          jobId={libreSpeedJobId}
          onJobIdChange={setLibreSpeedJobId}
          onOpenSettings={() => setActiveItem('settings')}
        />
      ) : (
        <SettingsPage theme={theme} onThemeChange={setTheme} session={session} onSessionChange={setSession} />
      )}
    </AppShell>
  )
}
