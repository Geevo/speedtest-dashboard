import { ArrowLeft } from 'lucide-react'
import type { NavigationItem } from './app-shell'
import { Button } from './ui/button'

const labels: Record<Exclude<NavigationItem, 'overview'>, string> = {
  ookla: 'Ookla',
  librespeed: 'LibreSpeed',
  iperf3: 'iPerf3',
  history: 'History',
  settings: 'Settings',
}

export function PlaceholderPage({ item, onBack }: { item: Exclude<NavigationItem, 'overview'>; onBack: () => void }) {
  return (
    <section className="page-enter">
      <header className="border-b border-line pb-6">
        <h1 className="text-[clamp(2rem,5vw,3.5rem)] font-semibold leading-none tracking-[-0.055em]">{labels[item]}</h1>
      </header>
      <div className="border-b border-line py-10">
        <p className="text-sm text-ink-muted">Not available yet.</p>
        <Button variant="outline" className="mt-6" onClick={onBack}>
          <ArrowLeft className="size-4" />
          Overview
        </Button>
      </div>
    </section>
  )
}
