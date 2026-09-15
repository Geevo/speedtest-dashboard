import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Copy, Database, HardDrive, KeyRound, Laptop, LoaderCircle, Moon, ShieldCheck, ShieldOff, Sun, Terminal, Trash2, X } from 'lucide-react'
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
import { deleteAllHistory } from '../lib/history'
import { compactDatabase, getDatabaseStorage, type DatabaseStorage } from '../lib/database'
import { cn } from '../lib/utils'
import { Button } from './ui/button'
import { Switch } from './ui/switch'
import { ApiReference } from './api-reference'

const choices = [
  { id: 'system' as const, label: 'System', icon: Laptop },
  { id: 'light' as const, label: 'Light', icon: Sun },
  { id: 'dark' as const, label: 'Dark', icon: Moon },
]

const inputClass = 'mt-2 h-11 w-full rounded-lg border border-line bg-paper px-3.5 text-base outline-none transition focus:border-signal focus:ring-3 focus:ring-signal/15'
const settingsRowClass = 'grid gap-5 md:grid-cols-[minmax(0,1fr)_auto] md:items-center md:gap-8 lg:gap-12'

type SettingsPageProps = {
  theme: ThemePreference
  onThemeChange: (theme: ThemePreference) => void
  session: SessionResponse
  onSessionChange: (session: SessionResponse) => void
}

export function SettingsPage({ theme, onThemeChange, session, onSessionChange }: SettingsPageProps) {
  const [tab, setTab] = useState<'general' | 'database' | 'api'>('general')
  const tabs = ['general', 'database', 'api'] as const

  return (
    <div className="settings-page page-enter">
      <header>
        <h1 className="page-title">Settings</h1>
      </header>
      <div role="tablist" aria-label="Settings" className="mt-6 flex gap-1 border-b border-line">
        {tabs.map((id) => (
          <button key={id} id={`settings-tab-${id}`} type="button" role="tab" aria-selected={tab === id}
            aria-controls={`settings-panel-${id}`} tabIndex={tab === id ? 0 : -1}
            onClick={() => setTab(id)}
            onKeyDown={(event) => {
              if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
                event.preventDefault()
                const currentIndex = tabs.indexOf(tab)
                const nextIndex = event.key === 'Home'
                  ? 0
                  : event.key === 'End'
                    ? tabs.length - 1
                    : event.key === 'ArrowRight'
                      ? (currentIndex + 1) % tabs.length
                      : (currentIndex - 1 + tabs.length) % tabs.length
                const next = tabs[nextIndex]
                setTab(next)
                document.getElementById(`settings-tab-${next}`)?.focus()
              }
            }}
            className={cn('min-h-11 border-b-2 px-5 text-sm font-semibold transition-colors', tab === id ? 'border-signal text-signal' : 'border-transparent text-ink-muted hover:border-ink-muted hover:text-ink')}>
            {id === 'api' ? 'API' : id === 'database' ? 'Database' : 'General'}
          </button>
        ))}
      </div>
      <div id="settings-panel-general" role="tabpanel" aria-labelledby="settings-tab-general" hidden={tab !== 'general'}>
        <section aria-labelledby="theme-heading" className="border-b border-line py-8">
          <div className={settingsRowClass}>
            <div>
              <h2 id="theme-heading" className="text-xl font-semibold tracking-[-0.03em]">Theme</h2>
              <p className="mt-1 max-w-2xl text-sm leading-6 text-ink-muted">Choose how the dashboard looks on this device.</p>
            </div>
            <div className="grid w-full grid-cols-3 overflow-hidden rounded-xl border border-line bg-paper md:w-96 lg:w-80 xl:w-[28rem]">
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
          </div>
        </section>
        <AuthenticationSection session={session} onSessionChange={onSessionChange} />
        {session.mode === 'local' && <PasswordSection />}
      </div>
      <div id="settings-panel-database" role="tabpanel" aria-labelledby="settings-tab-database" hidden={tab !== 'database'}>
        <DatabaseSection />
      </div>
      <div id="settings-panel-api" role="tabpanel" aria-labelledby="settings-tab-api" hidden={tab !== 'api'}>
        <ApiKeySection />
      </div>
    </div>
  )
}

function DatabaseSection() {
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [confirmation, setConfirmation] = useState('')
  const [deleting, setDeleting] = useState(false)
  const [status, setStatus] = useState<{ ok: boolean; message: string } | null>(null)
  const storage = useQuery({
    queryKey: ['database-storage'],
    queryFn: ({ signal }) => getDatabaseStorage(signal),
  })
  const compaction = useMutation({
    mutationFn: compactDatabase,
    onMutate: () => setStatus(null),
    onSuccess: (result) => {
      queryClient.setQueryData(['database-storage'], result.after)
      setStatus({
        ok: true,
        message: `Database compacted. Storage changed from ${formatBytes(result.before.totalBytes)} to ${formatBytes(result.after.totalBytes)}.`,
      })
    },
    onError: (reason) => {
      setStatus({ ok: false, message: reason instanceof ApiError ? reason.message : 'The database could not be compacted.' })
    },
  })

  const close = () => {
    setOpen(false)
    setConfirmation('')
  }

  const removeAll = async (event: FormEvent) => {
    event.preventDefault()
    if (confirmation !== 'DELETE ALL') return
    setDeleting(true)
    setStatus(null)
    try {
      const deletedCount = await deleteAllHistory()
      close()
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['history'] }),
        queryClient.invalidateQueries({ queryKey: ['history-detail'] }),
        queryClient.invalidateQueries({ queryKey: ['statistics'] }),
      ])
      setStatus({
        ok: true,
        message: deletedCount === 1 ? '1 speed-test result was deleted.' : `${deletedCount} speed-test results were deleted.`,
      })
    } catch (reason) {
      setStatus({ ok: false, message: reason instanceof Error ? reason.message : 'Speed-test results could not be deleted.' })
    } finally {
      setDeleting(false)
    }
  }

  return <>
    <section aria-labelledby="storage-heading" className="border-b border-line py-8">
      <div className={settingsRowClass}>
        <div className="flex min-w-0 items-start gap-3">
          <HardDrive className="mt-0.5 size-5 shrink-0 text-ink-muted" aria-hidden="true" />
          <div>
            <h2 id="storage-heading" className="text-xl font-semibold tracking-[-0.03em]">SQLite storage</h2>
            <p className="mt-1 max-w-2xl text-sm leading-6 text-ink-muted">
              Reclaim unused pages left behind after records are deleted. Compaction temporarily locks the database and may require additional free disk space while it runs.
            </p>
          </div>
        </div>
        <Button
          type="button"
          variant="outline"
          className="shrink-0 justify-self-start md:justify-self-end"
          disabled={compaction.isPending || storage.isLoading}
          onClick={() => compaction.mutate()}
        >
          {compaction.isPending ? <LoaderCircle className="size-4 animate-spin" aria-hidden="true" /> : <Database className="size-4" aria-hidden="true" />}
          {compaction.isPending ? 'Compacting…' : 'Compact database'}
        </Button>
      </div>

      <div className="mt-6 rounded-xl bg-canvas/65 p-4">
        <DatabaseSize storage={storage.data} loading={storage.isLoading} error={storage.isError} />
      </div>
      {storage.isError && (
        <Button type="button" variant="ghost" className="mt-3" onClick={() => void storage.refetch()}>Retry size check</Button>
      )}
      {status && <div className="mt-4"><StatusMessage status={status} /></div>}
    </section>

    <section aria-labelledby="database-heading" className="py-8">
      <div className={settingsRowClass}>
        <div className="flex min-w-0 items-start gap-3">
          <Database className="mt-0.5 size-5 shrink-0 text-ink-muted" aria-hidden="true" />
          <div>
            <h2 id="database-heading" className="text-xl font-semibold tracking-[-0.03em]">Speed-test results</h2>
            <p className="mt-1 max-w-2xl text-sm leading-6 text-ink-muted">
              Permanently remove every completed, failed, and cancelled speed-test result. Schedules and dashboard settings are kept.
            </p>
          </div>
        </div>
        <Button
          type="button"
          variant="outline"
          className="shrink-0 justify-self-start border-danger/40 text-danger hover:border-danger hover:bg-danger-soft md:justify-self-end"
          onClick={() => { setOpen(true); setStatus(null) }}
        >
          <Trash2 className="size-4" aria-hidden="true" />
          Delete all results
        </Button>
      </div>

      {open && (
        <SettingsDialog
          title="Delete all speed-test results?"
          description="This permanently deletes the full results history and cannot be undone. Schedules and dashboard settings will not be affected."
          onClose={close}
          busy={deleting}
        >
          <form className="space-y-5" onSubmit={removeAll}>
            <label className="block text-sm font-semibold">
              Type <strong>DELETE ALL</strong> to confirm
              <input
                autoFocus
                className={inputClass}
                value={confirmation}
                onChange={(event) => setConfirmation(event.target.value)}
                autoComplete="off"
                spellCheck={false}
              />
            </label>
            <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
              <Button type="button" variant="ghost" disabled={deleting} onClick={close}>Cancel</Button>
              <Button
                type="submit"
                disabled={deleting || confirmation !== 'DELETE ALL'}
                className="bg-danger text-white hover:bg-danger/90"
              >
                {deleting ? <LoaderCircle className="size-4 animate-spin" aria-hidden="true" /> : <Trash2 className="size-4" aria-hidden="true" />}
                {deleting ? 'Deleting…' : 'Delete all results'}
              </Button>
            </div>
          </form>
        </SettingsDialog>
      )}
    </section>
  </>
}

function DatabaseSize({ storage, loading, error }: {
  storage: DatabaseStorage | undefined
  loading: boolean
  error: boolean
}) {
  if (loading) return <p role="status" className="text-sm text-ink-muted">Calculating storage…</p>
  if (error || !storage) return <p role="alert" className="text-sm text-danger">Database storage could not be loaded.</p>

  return (
    <dl>
      <dt className="text-[10px] font-bold uppercase tracking-[0.14em] text-ink-muted">Current size</dt>
      <dd className="mt-1 text-2xl font-semibold tabular-nums tracking-[-0.03em]">{formatBytes(storage.totalBytes)}</dd>
      <dd className="mt-1 text-xs leading-5 text-ink-muted">
        Database {formatBytes(storage.databaseBytes)} · WAL {formatBytes(storage.writeAheadLogBytes)} · shared memory {formatBytes(storage.sharedMemoryBytes)}
      </dd>
    </dl>
  )
}

function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes} B`
  const units = ['KB', 'MB', 'GB', 'TB']
  const exponent = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length)
  const value = bytes / (1024 ** exponent)
  return `${new Intl.NumberFormat(undefined, { maximumFractionDigits: value >= 10 ? 1 : 2 }).format(value)} ${units[exponent - 1]}`
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
      <div className={settingsRowClass}>
        <div className="flex min-w-0 items-start gap-3">
          <Icon className="mt-0.5 size-5 shrink-0 text-ink-muted" aria-hidden="true" />
          <div>
            <h2 id="authentication-heading" className="text-xl font-semibold tracking-[-0.03em]">Login protection</h2>
            <p className="mt-1 max-w-2xl text-sm leading-6 text-ink-muted">
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
            className="shrink-0 justify-self-start whitespace-nowrap px-5 md:justify-self-end"
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
        <div className="mt-7 grid gap-5 border-t border-line pt-5 text-sm md:grid-cols-[minmax(0,1fr)_auto] md:items-center md:gap-8 lg:gap-12">
          <div className="md:pl-8">
            <p className="font-semibold">Anonymous-access notice</p>
            <p className="mt-1 max-w-2xl leading-5 text-ink-muted">Show a small reminder above the dashboard while login protection is off.</p>
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
      <div className={settingsRowClass}>
        <div className="flex min-w-0 items-start gap-3">
          <KeyRound className="mt-0.5 size-5 shrink-0 text-ink-muted" aria-hidden="true" />
          <div>
            <h2 id="password-heading" className="text-xl font-semibold tracking-[-0.03em]">Admin password</h2>
            <p className="mt-1 text-sm text-ink-muted">Update the password used to sign in to this dashboard.</p>
            {status && <div className="mt-3"><StatusMessage status={status} /></div>}
          </div>
        </div>
        <Button type="button" variant="outline" className="shrink-0 justify-self-start md:justify-self-end" onClick={() => { setOpen(true); setStatus(null) }}>
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
      <div className="flex min-w-0 items-start gap-3">
        <Terminal className="mt-0.5 size-5 shrink-0 text-ink-muted" aria-hidden="true" />
        <div>
          <h2 id="api-key-heading" className="text-xl font-semibold tracking-[-0.03em]">API access</h2>
          <p className="mt-1 max-w-2xl text-sm leading-6 text-ink-muted">
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

function StatusMessage({ status }: { status: { ok: boolean; message: string } }) {
  return <p role="status" className={status.ok ? 'text-sm text-ok' : 'text-sm text-danger'}>{status.message}</p>
}
