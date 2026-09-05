import { lazy, Suspense, useState } from 'react'
import { AppShell, type NavigationItem } from './components/app-shell'
import { OverviewPage } from './components/overview-page'
import { PlaceholderPage } from './components/placeholder-page'
import { LibreSpeedPage, OoklaPage } from './components/ookla-page'
import { SettingsPage } from './components/settings-page'
import { useTheme } from './hooks/use-theme'

const HistoryPage = lazy(() => import('./components/history-page').then((module) => ({ default: module.HistoryPage })))

export default function App() {
  const { theme, setTheme } = useTheme()
  const [activeItem, setActiveItem] = useState<NavigationItem>('overview')
  const [ooklaJobId, setOoklaJobId] = useState<string | null>(null)
  const [libreSpeedJobId, setLibreSpeedJobId] = useState<string | null>(null)

  return (
    <AppShell activeItem={activeItem} onNavigate={setActiveItem}>
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
      ) : activeItem === 'settings' ? (
        <SettingsPage theme={theme} onThemeChange={setTheme} />
      ) : (
        <PlaceholderPage item={activeItem} onBack={() => setActiveItem('overview')} />
      )}
    </AppShell>
  )
}
