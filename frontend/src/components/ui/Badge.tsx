import type { ReactNode } from 'react'

type Tone = 'store' | 'top' | 'new' | 'neutral'

const tones: Record<Tone, string> = {
  // Dark on the band colour, not white: white on it measures 1.88:1.
  store: 'bg-brand text-ink',
  top: 'bg-accent text-white',
  new: 'bg-olive text-white',
  neutral: 'bg-canvas text-muted border border-line',
}

interface BadgeProps {
  tone?: Tone
  children: ReactNode
}

/** Listing badges — the specification allows only a small, fixed set. */
export function Badge({ tone = 'neutral', children }: BadgeProps) {
  return (
    <span className={`inline-flex items-center rounded px-2 py-1 text-xs font-semibold ${tones[tone]}`}>
      {children}
    </span>
  )
}
