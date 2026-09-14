import { type ButtonHTMLAttributes } from 'react'
import { cn } from '../../lib/utils'

type SwitchProps = Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'onChange' | 'type' | 'role'> & {
  checked: boolean
  label: string
  onChange: (checked: boolean) => void
}

export function Switch({ checked, label, className, disabled = false, onChange, ...props }: SwitchProps) {
  return (
    <button
      {...props}
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={cn(
        'grid h-11 w-12 shrink-0 place-items-center rounded-lg focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-signal/25 disabled:cursor-not-allowed disabled:opacity-50',
        className,
      )}
    >
      <span
        aria-hidden="true"
        className={cn(
          'relative h-6 w-11 rounded-full transition-colors',
          checked ? 'bg-signal' : 'bg-line',
        )}
      >
        <span className={cn('absolute left-0.5 top-0.5 size-5 rounded-full bg-paper shadow-sm transition-transform', checked && 'translate-x-5')} />
      </span>
    </button>
  )
}
