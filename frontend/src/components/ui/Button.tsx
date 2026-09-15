import type { ButtonHTMLAttributes, ReactNode } from 'react'

type Variant = 'primary' | 'secondary' | 'ghost' | 'accent'
type Size = 'sm' | 'md' | 'lg'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: Size
  children: ReactNode
}

const variants: Record<Variant, string> = {
  // Hover lightens rather than switching to the band colour, which is now too light to carry the
  // white label on it.
  primary: 'bg-interactive text-white hover:brightness-125',
  secondary: 'bg-surface text-ink border border-line hover:border-interactive',
  ghost: 'bg-transparent text-interactive hover:bg-interactive-soft',
  // Reserved for the primary conversion action — "Yeni elan" — per the design rules.
  accent: 'bg-cta text-white hover:brightness-95',
}

const sizes: Record<Size, string> = {
  sm: 'h-9 px-3 text-sm',
  md: 'h-11 px-4 text-[15px]',
  lg: 'h-12 px-5 text-base',
}

export function Button({ variant = 'primary', size = 'md', className = '', children, ...props }: ButtonProps) {
  return (
    <button
      className={`inline-flex items-center justify-center gap-2 rounded-(--radius-button) font-semibold transition-colors disabled:cursor-not-allowed disabled:opacity-45 ${variants[variant]} ${sizes[size]} ${className}`}
      {...props}
    >
      {children}
    </button>
  )
}
