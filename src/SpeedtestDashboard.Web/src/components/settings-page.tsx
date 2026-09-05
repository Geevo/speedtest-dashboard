import { useState, type FormEvent } from 'react'
import { KeyRound, Laptop, Moon, ShieldCheck, ShieldOff, Sun } from 'lucide-react'
import type { ThemePreference } from '../hooks/use-theme'
import {
  ApiError,
  changePassword,
  disableLogin,
  enableLogin,
  setDisabledWarningVisible,
  type SessionResponse,
} from '../lib/api'
import { cn } from '../lib/utils'
import { Button } from './ui/button'

const choices = [
  { id: 'system' as const, label: 'System', icon: Laptop },
  { id: 'light' as const, label: 'Light', icon: Sun },
  { id: 'dark' as const, label: 'Dark', icon: Moon },
]

const inputClass = 'mt-2 h-11 w-full rounded-lg border border-line bg-paper px-3.5 text-base outline-none transition focus:border-signal focus:ring-3 focus:ring-signal/15'

type SettingsPageProps = {
  theme: ThemePreference
  onThemeChange: (theme: ThemePreference) => void
  session: SessionResponse
  onSessionChange: (session: SessionResponse) => void
}

export function SettingsPage({ theme, onThemeChange, session, onSessionChange }: SettingsPageProps) {
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
      <AuthenticationSection session={session} onSessionChange={onSessionChange} />
      {session.mode === 'local' && <PasswordSection />}
    </div>
  )
}

function AuthenticationSection({ session, onSessionChange }: Pick<SettingsPageProps, 'session' | 'onSessionChange'>) {
  const enabled = session.mode === 'local'
  const [editing, setEditing] = useState(false)
  const [username, setUsername] = useState('admin')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [currentPassword, setCurrentPassword] = useState('')
  const [status, setStatus] = useState<{ ok: boolean; message: string } | null>(null)
  const [saving, setSaving] = useState(false)

  const enable = async (event: FormEvent) => {
    event.preventDefault()
    if (!session.loginConfigured && password !== confirmPassword) {
      setStatus({ ok: false, message: 'Password and confirmation do not match.' })
      return
    }
    setSaving(true)
    setStatus(null)
    try {
      onSessionChange(await enableLogin(username, password))
      setEditing(false)
      setPassword('')
      setConfirmPassword('')
    } catch (reason) {
      setStatus({ ok: false, message: reason instanceof ApiError ? reason.message : 'Login protection could not be enabled.' })
    } finally {
      setSaving(false)
    }
  }

  const disable = async (event: FormEvent) => {
    event.preventDefault()
    setSaving(true)
    setStatus(null)
    try {
      onSessionChange(await disableLogin(currentPassword))
      setEditing(false)
      setCurrentPassword('')
    } catch (reason) {
      setStatus({ ok: false, message: reason instanceof ApiError ? reason.message : 'Login protection could not be disabled.' })
    } finally {
      setSaving(false)
    }
  }

  const setWarning = async (visible: boolean) => {
    setSaving(true)
    setStatus(null)
    try {
      onSessionChange(await setDisabledWarningVisible(visible))
    } catch {
      setStatus({ ok: false, message: 'The notice preference could not be saved.' })
    } finally {
      setSaving(false)
    }
  }

  const Icon = enabled ? ShieldCheck : ShieldOff
  return (
    <section aria-labelledby="authentication-heading" className="border-b border-line py-8">
      <div className={cn(
        'flex max-w-2xl items-start gap-5',
        session.loginConfigured ? 'justify-between' : 'flex-col sm:flex-row sm:justify-between',
      )}>
        <div className="flex items-start gap-3">
          <Icon className="mt-0.5 size-5 text-ink-muted" aria-hidden="true" />
          <div>
            <h2 id="authentication-heading" className="text-xl font-semibold tracking-[-0.03em]">Login protection</h2>
            <p className="mt-1 max-w-lg text-sm leading-6 text-ink-muted">
              {enabled
                ? 'A password is required before anyone can use this dashboard.'
                : session.loginConfigured
                  ? 'This dashboard opens directly. Your local account is ready if you want to turn login back on.'
                  : 'This dashboard opens directly. Set up a local account only if you want to require a login.'}
            </p>
          </div>
        </div>
        {session.loginConfigured ? (
          <Switch
            checked={enabled}
            label={enabled ? 'Turn off login protection' : 'Turn on login protection'}
            onChange={() => { setEditing(true); setStatus(null) }}
          />
        ) : (
          <Button
            type="button"
            className="shrink-0 whitespace-nowrap px-5"
            onClick={() => { setEditing(true); setStatus(null) }}
          >
            Set up login
          </Button>
        )}
      </div>

      {editing && !enabled && (
        <form className="mt-6 max-w-xl space-y-4" onSubmit={enable}>
          <div>
            <p className="text-sm font-semibold">{session.loginConfigured ? 'Turn login back on' : 'Create a local account'}</p>
            <p className="mt-1 text-sm leading-5 text-ink-muted">
              {session.loginConfigured ? 'Enter the existing account details to confirm this change.' : 'This account stays on this instance and can be removed from the login flow at any time.'}
            </p>
          </div>
          <label className="block text-sm font-semibold">Username
            <input className={inputClass} autoComplete="username" value={username} onChange={(event) => setUsername(event.target.value)} required maxLength={64} />
          </label>
          <div className={cn('grid gap-4', !session.loginConfigured && 'sm:grid-cols-2')}>
            <label className="block text-sm font-semibold">Password
              <input className={inputClass} type="password" autoComplete={session.loginConfigured ? 'current-password' : 'new-password'} value={password} onChange={(event) => setPassword(event.target.value)} required minLength={6} maxLength={128} />
            </label>
            {!session.loginConfigured && (
              <label className="block text-sm font-semibold">Confirm password
                <input className={inputClass} type="password" autoComplete="new-password" value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} required minLength={6} maxLength={128} />
              </label>
            )}
          </div>
          {status && <StatusMessage status={status} />}
          <div className="flex gap-2">
            <Button type="submit" disabled={saving}>{saving ? 'Turning on…' : 'Turn on login'}</Button>
            <Button variant="ghost" onClick={() => setEditing(false)}>Cancel</Button>
          </div>
        </form>
      )}

      {editing && enabled && (
        <form className="mt-6 max-w-xl space-y-4" onSubmit={disable}>
          <p className="text-sm leading-6 text-ink-muted">Enter your password to return this instance to anonymous access.</p>
          <label className="block text-sm font-semibold">Current password
            <input className={inputClass} type="password" autoComplete="current-password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} required maxLength={128} />
          </label>
          {status && <StatusMessage status={status} />}
          <div className="flex gap-2">
            <Button type="submit" disabled={saving}>{saving ? 'Turning off…' : 'Turn off login'}</Button>
            <Button variant="ghost" onClick={() => setEditing(false)}>Cancel</Button>
          </div>
        </form>
      )}

      {!enabled && (
        <div className="mt-7 flex max-w-2xl items-start justify-between gap-5 border-t border-line pt-5 text-sm">
          <div>
            <p className="font-semibold">Anonymous-access notice</p>
            <p className="mt-1 max-w-lg leading-5 text-ink-muted">Show a small reminder above the dashboard while login protection is off.</p>
          </div>
          <Switch
            checked={session.showDisabledWarning}
            disabled={saving}
            label="Show anonymous-access notice"
            onChange={(visible) => void setWarning(visible)}
          />
        </div>
      )}
    </section>
  )
}

function PasswordSection() {
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [status, setStatus] = useState<{ ok: boolean; message: string } | null>(null)
  const [saving, setSaving] = useState(false)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (newPassword !== confirmPassword) {
      setStatus({ ok: false, message: 'New password and confirmation do not match.' })
      return
    }
    setSaving(true)
    setStatus(null)
    try {
      await changePassword(currentPassword, newPassword)
      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
      setStatus({ ok: true, message: 'Password changed. This browser remains signed in.' })
    } catch (reason) {
      setStatus({ ok: false, message: reason instanceof ApiError ? reason.message : 'The password could not be changed.' })
    } finally {
      setSaving(false)
    }
  }

  return (
    <section aria-labelledby="password-heading" className="py-8">
      <div className="flex items-start gap-3">
        <KeyRound className="mt-0.5 size-5 text-ink-muted" aria-hidden="true" />
        <div>
          <h2 id="password-heading" className="text-xl font-semibold tracking-[-0.03em]">Change password</h2>
          <p className="mt-1 text-sm text-ink-muted">Use 6–128 characters. There are no composition rules.</p>
        </div>
      </div>
      <form className="mt-6 max-w-xl space-y-4" onSubmit={submit}>
        <label className="block text-sm font-semibold">Current password
          <input className={inputClass} type="password" autoComplete="current-password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} required maxLength={128} />
        </label>
        <div className="grid gap-4 sm:grid-cols-2">
          <label className="block text-sm font-semibold">New password
            <input className={inputClass} type="password" autoComplete="new-password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)} required minLength={6} maxLength={128} />
          </label>
          <label className="block text-sm font-semibold">Confirm new password
            <input className={inputClass} type="password" autoComplete="new-password" value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} required minLength={6} maxLength={128} />
          </label>
        </div>
        {status && <StatusMessage status={status} />}
        <Button type="submit" disabled={saving}>{saving ? 'Changing…' : 'Change password'}</Button>
      </form>
    </section>
  )
}

function Switch({ checked, disabled = false, label, onChange }: {
  checked: boolean
  disabled?: boolean
  label: string
  onChange: (checked: boolean) => void
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={cn(
        'relative mt-0.5 h-7 w-12 shrink-0 rounded-full transition-colors focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-signal/25 disabled:cursor-not-allowed disabled:opacity-50',
        checked ? 'bg-signal' : 'bg-line',
      )}
    >
      <span className={cn('absolute left-0 top-1 size-5 rounded-full bg-paper shadow-sm transition-transform', checked ? 'translate-x-6' : 'translate-x-1')} />
    </button>
  )
}

function StatusMessage({ status }: { status: { ok: boolean; message: string } }) {
  return <p role="status" className={status.ok ? 'text-sm text-ok' : 'text-sm text-danger'}>{status.message}</p>
}
