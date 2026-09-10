import type { ReactNode, SelectHTMLAttributes } from 'react'
import { useId } from 'react'

interface SelectProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, 'id'> {
  label: string
  error?: string | undefined
  hint?: string | undefined
  /** Shown as a disabled first option when nothing is chosen yet. */
  placeholder?: string
  children: ReactNode
}

export function Select({
  label,
  error,
  hint,
  placeholder,
  className = '',
  children,
  ...props
}: SelectProps) {
  const id = useId()
  const errorId = `${id}-error`
  const hintId = `${id}-hint`

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-medium text-ink">
        {label}
      </label>

      <select
        id={id}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : hint ? hintId : undefined}
        className={`h-11 rounded-(--radius-input) border bg-surface px-3 text-[15px] outline-none transition-colors ${
          error ? 'border-accent' : 'border-line focus:border-interactive'
        } disabled:bg-canvas disabled:text-faint ${className}`}
        {...props}
      >
        {placeholder ? (
          <option value="" disabled>
            {placeholder}
          </option>
        ) : null}

        {children}
      </select>

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
