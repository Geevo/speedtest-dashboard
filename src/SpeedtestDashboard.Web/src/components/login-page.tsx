import { useState, type FormEvent } from 'react'
import { ChartNoAxesCombined, LockKeyhole } from 'lucide-react'
import { ApiError, login, type SessionResponse } from '../lib/api'
import { Button } from './ui/button'

export function LoginPage({ onSignedIn }: { onSignedIn: (session: SessionResponse) => void }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<{ title: string; message: string } | null>(null)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (submitting) return
    setSubmitting(true)
    setError(null)
    try {
      onSignedIn(await login(username, password))
    } catch (reason) {
      if (reason instanceof ApiError && reason.code === 'https_required') {
        setError({
          title: 'Secure access required',
          message: 'Open this dashboard through its HTTPS reverse proxy. Insecure HTTP can only be enabled explicitly for a trusted network.',
        })
      } else if (reason instanceof ApiError && reason.status === 401) {
        setError({ title: 'Sign in failed', message: 'Check the username and password, then try again.' })
      } else if (reason instanceof ApiError && reason.status === 429) {
        setError({ title: 'Too many attempts', message: 'Wait a minute before trying again.' })
      } else {
        setError({ title: 'Dashboard unavailable', message: 'The dashboard could not complete sign in. Try again shortly.' })
      }
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <main className="grid min-h-screen bg-canvas px-5 py-8 text-ink sm:px-8 lg:grid-cols-[minmax(20rem,0.85fr)_minmax(28rem,1.15fr)] lg:p-0">
      <section className="relative hidden overflow-hidden border-r border-line bg-ink text-canvas lg:flex lg:flex-col lg:justify-between lg:p-14">
        <div className="flex items-center gap-3">
          <div className="grid size-10 place-items-center rounded-xl bg-canvas text-ink">
            <ChartNoAxesCombined className="size-5" aria-hidden="true" />
          </div>
          <span className="font-semibold tracking-[-0.02em]">Speedtest Dashboard</span>
        </div>
        <div className="max-w-md">
          <div className="mb-6 h-px w-20 bg-canvas/35" />
          <p className="text-[clamp(2rem,4vw,4rem)] font-semibold leading-[0.96] tracking-[-0.06em]">
            Your network,<br />measured here.
          </p>
          <p className="mt-6 max-w-sm text-sm leading-6 text-canvas/65">
            Sign in to view this instance&apos;s egress identity, run tests, and inspect saved results.
          </p>
        </div>
        <p className="text-xs text-canvas/45">Cookie-secured local session</p>
      </section>

      <section className="flex items-center justify-center lg:px-16">
        <div className="w-full max-w-[25rem] page-enter">
          <div className="mb-12 flex items-center gap-3 lg:hidden">
            <div className="grid size-9 place-items-center rounded-xl bg-ink text-canvas">
              <ChartNoAxesCombined className="size-5" aria-hidden="true" />
            </div>
            <span className="font-semibold">Speedtest Dashboard</span>
          </div>
          <LockKeyhole className="mb-6 size-6 text-ink-muted" strokeWidth={1.7} aria-hidden="true" />
          <h1 className="text-[clamp(2.25rem,7vw,3.5rem)] font-semibold leading-none tracking-[-0.055em]">Sign in</h1>
          <p className="mt-4 text-sm leading-6 text-ink-muted">Use the local operator account for this dashboard.</p>

          <form className="mt-9 space-y-5" onSubmit={submit}>
            <label className="block">
              <span className="mb-2 block text-sm font-semibold">Username</span>
              <input
                autoComplete="username"
                autoFocus
                value={username}
                onChange={(event) => setUsername(event.target.value)}
                className="h-12 w-full rounded-lg border border-line bg-paper px-3.5 text-base outline-none transition focus:border-signal focus:ring-3 focus:ring-signal/15"
                required
                maxLength={64}
              />
            </label>
            <label className="block">
              <span className="mb-2 block text-sm font-semibold">Password</span>
              <input
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                className="h-12 w-full rounded-lg border border-line bg-paper px-3.5 text-base outline-none transition focus:border-signal focus:ring-3 focus:ring-signal/15"
                required
                maxLength={128}
              />
            </label>

            {error && (
              <div role="alert" className="border-l-2 border-danger bg-danger-soft px-4 py-3 text-sm">
                <p className="font-semibold">{error.title}</p>
                <p className="mt-1 leading-5 text-ink-muted">{error.message}</p>
              </div>
            )}

            <Button type="submit" className="h-12 w-full" disabled={submitting}>
              {submitting ? 'Signing in…' : 'Sign in'}
            </Button>
          </form>
        </div>
      </section>
    </main>
  )
}
