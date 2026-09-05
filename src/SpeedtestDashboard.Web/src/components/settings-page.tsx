import { Laptop, Moon, Sun } from 'lucide-react'
import type { ThemePreference } from '../hooks/use-theme'
import { cn } from '../lib/utils'

const choices = [
  { id: 'system' as const, label: 'System', icon: Laptop },
  { id: 'light' as const, label: 'Light', icon: Sun },
  { id: 'dark' as const, label: 'Dark', icon: Moon },
]

export function SettingsPage({ theme, onThemeChange }: { theme: ThemePreference; onThemeChange: (theme: ThemePreference) => void }) {
  return (
    <div className="page-enter">
      <header className="border-b border-line pb-6">
        <h1 className="text-[clamp(2rem,5vw,3.5rem)] font-semibold leading-none tracking-[-0.055em]">Settings</h1>
      </header>
      <section aria-labelledby="theme-heading" className="border-b border-line py-8">
        <h2 id="theme-heading" className="text-xl font-semibold tracking-[-0.03em]">Theme</h2>
        <div className="mt-5 grid max-w-xl grid-cols-3 overflow-hidden rounded-xl border border-line bg-paper">
          {choices.map(({ id, label, icon: Icon }) => (
            <button
              key={id}
              type="button"
              aria-pressed={theme === id}
              className={cn(
                'flex min-h-14 items-center justify-center gap-2 border-r border-line px-3 text-sm font-semibold last:border-r-0',
                theme === id ? 'bg-ink text-canvas' : 'text-ink-muted hover:bg-canvas hover:text-ink',
              )}
              onClick={() => onThemeChange(id)}
            >
              <Icon className="size-4" />
              {label}
            </button>
          ))}
        </div>
      </section>
    </div>
  )
}
