import type { HTMLAttributes } from 'react'
import { cn } from '../lib/utils'

export function SpeedDialMark({ className, ...props }: HTMLAttributes<HTMLSpanElement>) {
  return (
    <span className={cn('brand-tile', className)} {...props}>
      <img src="/brand-mark.png" alt="" />
    </span>
  )
}
