import type { InputHTMLAttributes, ReactNode } from 'react'
import { useId } from 'react'

interface InputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  label: string
  error?: string | undefined
  hint?: ReactNode
}

export function Input({ label, error, hint, className = '', ...props }: InputProps) {
  const id = useId()
  const errorId = `${id}-error`
  const hintId = `${id}-hint`

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-medium text-ink">
        {label}
      </label>

      <input
        id={id}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : hint ? hintId : undefined}
        className={`h-11 rounded-(--radius-input) border bg-surface px-3 text-[15px] outline-none transition-colors ${
          error ? 'border-accent' : 'border-line focus:border-interactive'
        } ${className}`}
        {...props}
      />

      {error ? (
        <p id={errorId} role="alert" className="text-sm text-accent">
          {error}
        </p>
      ) : hint ? (
        <p id={hintId} className="text-sm text-muted">
          {hint}
        </p>
      ) : null}
    </div>
  )
}
