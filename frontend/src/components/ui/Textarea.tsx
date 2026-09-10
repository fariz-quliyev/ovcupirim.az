import type { TextareaHTMLAttributes } from 'react'
import { useId } from 'react'

interface TextareaProps extends Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'id'> {
  label: string
  error?: string | undefined
  hint?: string | undefined
  /** Shows a live "used / allowed" counter, the way a long-description field should. */
  counter?: boolean
}

export function Textarea({
  label,
  error,
  hint,
  counter = false,
  className = '',
  value,
  maxLength,
  ...props
}: TextareaProps) {
  const id = useId()
  const errorId = `${id}-error`
  const hintId = `${id}-hint`
  const used = typeof value === 'string' ? value.length : 0

  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-baseline justify-between gap-2">
        <label htmlFor={id} className="text-sm font-medium text-ink">
          {label}
        </label>

        {counter && maxLength ? (
          <span className="text-xs text-muted tabular-nums">
            {used} / {maxLength}
          </span>
        ) : null}
      </div>

      <textarea
        id={id}
        value={value}
        maxLength={maxLength}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : hint ? hintId : undefined}
        className={`min-h-32 rounded-(--radius-input) border bg-surface px-3 py-2 text-[15px] outline-none transition-colors ${
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
