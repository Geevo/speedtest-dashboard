import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Check, Copy, KeyRound, Laptop, Moon, ShieldCheck, ShieldOff, Sun, Terminal, X } from 'lucide-react'
import type { ThemePreference } from '../hooks/use-theme'
import {
  ApiError,
  changePassword,
  disableLogin,
  enableLogin,
  getApiKey,
  regenerateApiKey,
  revokeApiKey,
  setDisabledWarningVisible,
  type ApiKeyResponse,
  type SessionResponse,
} from '../lib/api'
import { cn } from '../lib/utils'
import { Button } from './ui/button'
import { ApiReference } from './api-reference'

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
  const [tab, setTab] = useState<'general' | 'api'>('general')

  return (
    <div className="page-enter">
      <header className="border-b border-line pb-6">
        <h1 className="text-[clamp(2rem,5vw,3.5rem)] font-semibold leading-none tracking-[-0.055em]">Settings</h1>
      </header>
      <div role="tablist" aria-label="Settings" className="mt-6 flex gap-1 border-b border-line">
        {(['general', 'api'] as const).map((id) => (
          <button key={id} id={`settings-tab-${id}`} type="button" role="tab" aria-selected={tab === id}
            aria-controls={`settings-panel-${id}`} tabIndex={tab === id ? 0 : -1}
            onClick={() => setTab(id)}
            onKeyDown={(event) => {
              if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
                event.preventDefault()
                const next = event.key === 'Home' ? 'general' : event.key === 'End' ? 'api' : tab === 'general' ? 'api' : 'general'
                setTab(next)
                document.getElementById(`settings-tab-${next}`)?.focus()
              }
            }}
            className={cn('min-h-11 border-b-2 px-5 text-sm font-semibold transition-colors', tab === id ? 'border-signal text-signal' : 'border-transparent text-ink-muted hover:border-ink-muted hover:text-ink')}>
            {id === 'api' ? 'API' : 'General'}
          </button>
        ))}
      </div>
      <div id="settings-panel-general" role="tabpanel" aria-labelledby="settings-tab-general" hidden={tab !== 'general'}>
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
      <div id="settings-panel-api" role="tabpanel" aria-labelledby="settings-tab-api" hidden={tab !== 'api'}>
        <ApiKeySection />
      </div>
    </div>
  )
}

function AuthenticationSection({ session, onSessionChange }: Pick<SettingsPageProps, 'session' | 'onSessionChange'>) {
  const enabled = session.mode === 'local'
  const [dialog, setDialog] = useState<'enable' | 'disable' | null>(null)
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
      setDialog(null)
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
      setDialog(null)
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
                  : 'This dashboard opens directly. Create a local account when you want to require a login.'}
            </p>
          </div>
        </div>
        {session.loginConfigured ? (
          <Switch
            checked={enabled}
            label={enabled ? 'Turn off login protection' : 'Turn on login protection'}
            onChange={() => { setDialog(enabled ? 'disable' : 'enable'); setStatus(null) }}
          />
        ) : (
          <Button
            type="button"
            className="shrink-0 whitespace-nowrap px-5"
            onClick={() => { setDialog('enable'); setStatus(null) }}
          >
            Set up login
          </Button>
        )}
      </div>

      {dialog === 'enable' && (
        <SettingsDialog
          title={session.loginConfigured ? 'Turn login back on' : 'Create admin account'}
          description={session.loginConfigured
            ? 'Enter the existing account details to protect this dashboard again.'
            : 'Create the local credentials used to administer this dashboard.'}
          onClose={() => setDialog(null)}
          busy={saving}
        >
          <form className="space-y-4" onSubmit={enable}>
            <label className="block text-sm font-semibold">Username
              <input autoFocus className={inputClass} autoComplete="username" value={username} onChange={(event) => setUsername(event.target.value)} required maxLength={64} />
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
            <div className="flex flex-col-reverse gap-2 pt-1 sm:flex-row sm:justify-end">
              <Button type="button" variant="ghost" disabled={saving} onClick={() => setDialog(null)}>Cancel</Button>
              <Button type="submit" disabled={saving}>{saving ? 'Turning on…' : 'Turn on login'}</Button>
            </div>
          </form>
        </SettingsDialog>
      )}

      {dialog === 'disable' && (
        <SettingsDialog
          title="Turn off login protection?"
          description="The dashboard will become anonymously accessible. The local admin account and its password will be permanently deleted."
          onClose={() => setDialog(null)}
          busy={saving}
        >
          <form className="space-y-4" onSubmit={disable}>
            <label className="block text-sm font-semibold">Current password
              <input autoFocus className={inputClass} type="password" autoComplete="current-password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} required maxLength={128} />
            </label>
            <p className="text-sm leading-6 text-ink-muted">You can create a new admin account and password later.</p>
            {status && <StatusMessage status={status} />}
            <div className="flex flex-col-reverse gap-2 pt-1 sm:flex-row sm:justify-end">
              <Button type="button" variant="ghost" disabled={saving} onClick={() => setDialog(null)}>Cancel</Button>
              <Button type="submit" disabled={saving}>{saving ? 'Turning off…' : 'Turn off and delete account'}</Button>
            </div>
          </form>
        </SettingsDialog>
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
  const [open, setOpen] = useState(false)
  const [status, setStatus] = useState<{ ok: boolean; message: string } | null>(null)

  return (
    <section aria-labelledby="password-heading" className="py-8">
      <div className="flex max-w-2xl flex-col items-start gap-5 sm:flex-row sm:justify-between">
        <div className="flex items-start gap-3">
          <KeyRound className="mt-0.5 size-5 text-ink-muted" aria-hidden="true" />
          <div>
            <h2 id="password-heading" className="text-xl font-semibold tracking-[-0.03em]">Admin password</h2>
            <p className="mt-1 text-sm text-ink-muted">Update the password used to sign in to this dashboard.</p>
            {status && <div className="mt-3"><StatusMessage status={status} /></div>}
          </div>
        </div>
        <Button type="button" variant="outline" className="shrink-0" onClick={() => { setOpen(true); setStatus(null) }}>
          Change password
        </Button>
      </div>
      {open && <PasswordDialog onClose={() => setOpen(false)} onChanged={() => {
        setOpen(false)
        setStatus({ ok: true, message: 'Password changed. This browser remains signed in.' })
      }} />}
    </section>
  )
}

function PasswordDialog({ onClose, onChanged }: { onClose: () => void; onChanged: () => void }) {
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
      onChanged()
    } catch (reason) {
      setStatus({ ok: false, message: reason instanceof ApiError ? reason.message : 'The password could not be changed.' })
    } finally {
      setSaving(false)
    }
  }

  return (
    <SettingsDialog
      title="Change admin password"
      description="Use 6–128 characters. There are no composition rules."
      onClose={onClose}
      busy={saving}
    >
      <form className="space-y-4" onSubmit={submit}>
        <label className="block text-sm font-semibold">Current password
          <input autoFocus className={inputClass} type="password" autoComplete="current-password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} required maxLength={128} />
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
        <div className="flex flex-col-reverse gap-2 pt-1 sm:flex-row sm:justify-end">
          <Button type="button" variant="ghost" disabled={saving} onClick={onClose}>Cancel</Button>
          <Button type="submit" disabled={saving}>{saving ? 'Changing…' : 'Change password'}</Button>
        </div>
      </form>
    </SettingsDialog>
  )
}

function SettingsDialog({ title, description, busy, onClose, children }: {
  title: string
  description: string
  busy: boolean
  onClose: () => void
  children: ReactNode
}) {
  const dialogRef = useRef<HTMLDialogElement>(null)
  const titleId = `settings-dialog-title-${title.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`
  const descriptionId = `${titleId}-description`

  useEffect(() => {
    const dialog = dialogRef.current
    if (!dialog) return
    const opener = document.activeElement
    dialog.showModal()
    return () => {
      dialog.close()
      if (opener instanceof HTMLElement) requestAnimationFrame(() => opener.focus())
    }
  }, [])

  return createPortal(
    <dialog
      ref={dialogRef}
      className="settings-dialog m-auto max-h-[min(92dvh,42rem)] w-[min(34rem,calc(100%-1.5rem))] overflow-hidden rounded-2xl border border-line bg-paper p-0 text-ink shadow-2xl"
      aria-labelledby={titleId}
      aria-describedby={descriptionId}
      onCancel={(event) => {
        event.preventDefault()
        if (!busy) onClose()
      }}
      onClick={(event) => {
        if (event.target === event.currentTarget && !busy) onClose()
      }}
    >
      <header className="flex items-start justify-between gap-6 border-b border-line px-5 py-5 sm:px-7">
        <div>
          <h2 id={titleId} className="text-xl font-semibold tracking-[-0.03em]">{title}</h2>
          <p id={descriptionId} className="mt-1 text-sm leading-6 text-ink-muted">{description}</p>
        </div>
        <Button type="button" variant="ghost" size="icon" className="-mr-2 -mt-2 shrink-0" aria-label="Close dialog" disabled={busy} onClick={onClose}>
          <X className="size-5" aria-hidden="true" />
        </Button>
      </header>
      <div className="overflow-y-auto px-5 py-6 sm:px-7">{children}</div>
    </dialog>,
    document.body,
  )
}

function ApiKeySection() {
  const [state, setState] = useState<ApiKeyResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [copied, setCopied] = useState(false)
  const [status, setStatus] = useState<{ ok: boolean; message: string } | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    let active = true
    getApiKey(controller.signal)
      .then((response) => {
        if (active) setState(response)
      })
      .catch(() => {
        if (active) setStatus({ ok: false, message: 'The API key status could not be loaded.' })
      })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => {
      active = false
      controller.abort()
    }
  }, [])

  const generate = async () => {
    setBusy(true)
    setStatus(null)
    setCopied(false)
    try {
      setState(await regenerateApiKey())
    } catch (reason) {
      setStatus({ ok: false, message: reason instanceof ApiError ? reason.message : 'The API key could not be generated.' })
    } finally {
      setBusy(false)
    }
  }

  const revoke = async () => {
    setBusy(true)
    setStatus(null)
    try {
      await revokeApiKey()
      setState({ enabled: false, key: null, createdAtUtc: null, lastUsedAtUtc: null })
    } catch (reason) {
      setStatus({ ok: false, message: reason instanceof ApiError ? reason.message : 'The API key could not be revoked.' })
    } finally {
      setBusy(false)
    }
  }

  const copy = async () => {
    if (!state?.key) return
    try {
      await navigator.clipboard.writeText(state.key)
      setCopied(true)
    } catch {
      setStatus({ ok: false, message: 'The key could not be copied to the clipboard.' })
    }
  }

  return (
    <section aria-labelledby="api-key-heading" className="py-8">
      <div className="flex items-start gap-3">
        <Terminal className="mt-0.5 size-5 text-ink-muted" aria-hidden="true" />
        <div>
          <h2 id="api-key-heading" className="text-xl font-semibold tracking-[-0.03em]">API access</h2>
          <p className="mt-1 max-w-lg text-sm leading-6 text-ink-muted">
            One instance-wide key authenticates machine clients against the stable <code>/api/v1</code> surface. It has no roles or scopes, and it can be viewed again at any time.
          </p>
        </div>
      </div>

      {loading && <p role="status" className="mt-6 text-sm text-ink-muted">Loading API access…</p>}
      {status && <div className="mt-4"><StatusMessage status={status} /></div>}

      {!loading && state && !state.enabled && (
        <div className="mt-6 max-w-xl">
          <p className="text-sm text-ink-muted">API access is disabled.</p>
          <Button type="button" className="mt-3" disabled={busy} onClick={() => void generate()}>
            {busy ? 'Generating…' : 'Generate API key'}
          </Button>
        </div>
      )}

      {!loading && state && state.enabled && (
        <div className="mt-6 max-w-2xl space-y-4">
          <div>
            <div className="flex items-center justify-between gap-4">
              <label htmlFor="api-key-value" className="text-sm font-semibold">API key</label>
              <span className="inline-flex items-center gap-1.5 text-xs font-semibold text-ok">
                <span className="size-1.5 rounded-full bg-ok" aria-hidden="true" />
                Enabled
              </span>
            </div>
            <div className="mt-2 flex h-13 overflow-hidden rounded-xl border border-ink/20 bg-canvas transition focus-within:border-signal focus-within:ring-3 focus-within:ring-signal/15">
              <input
                id="api-key-value"
                className="min-w-0 flex-1 bg-transparent px-4 font-mono text-sm text-ink outline-none"
                type="text"
                value={state.key ?? ''}
                readOnly
                spellCheck={false}
                onFocus={(event) => event.currentTarget.select()}
              />
              <div className="flex shrink-0 items-center border-l border-line p-1">
                <Button type="button" variant="ghost" aria-label="Copy API key" onClick={() => void copy()}>
                  {copied ? <Check className="size-4 text-ok" aria-hidden="true" /> : <Copy className="size-4" aria-hidden="true" />}
                  {copied ? 'Copied' : 'Copy key'}
                </Button>
              </div>
            </div>
            <p className="mt-2 text-xs leading-5 text-ink-muted">Treat this key like a password. Clients send it as a bearer token.</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="ghost" disabled={busy} onClick={() => void generate()}>
              {busy ? 'Regenerating…' : 'Regenerate'}
            </Button>
            <Button type="button" variant="ghost" disabled={busy} onClick={() => void revoke()}>
              {busy ? 'Revoking…' : 'Revoke'}
            </Button>
          </div>
        </div>
      )}
      {state?.enabled && <ApiReference />}
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
        'relative flex h-11 w-24 shrink-0 items-center rounded-full border-2 transition-colors focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-signal/25 disabled:cursor-not-allowed disabled:opacity-40',
        checked ? 'border-signal bg-signal text-canvas' : 'border-ink-muted bg-paper text-ink hover:border-ink',
      )}
    >
      <span aria-hidden="true" className={cn('absolute left-0 top-2 size-6 rounded-full transition-transform', checked ? 'translate-x-15 bg-canvas' : 'translate-x-2 bg-ink-muted')} />
      <span aria-hidden="true" className={cn('text-xs font-semibold', checked ? 'ml-4' : 'ml-11')}>{checked ? 'On' : 'Off'}</span>
    </button>
  )
}

function StatusMessage({ status }: { status: { ok: boolean; message: string } }) {
  return <p role="status" className={status.ok ? 'text-sm text-ok' : 'text-sm text-danger'}>{status.message}</p>
}
