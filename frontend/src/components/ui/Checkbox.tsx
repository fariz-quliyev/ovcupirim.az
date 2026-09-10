import type { InputHTMLAttributes, ReactNode } from 'react'
import { useId } from 'react'

interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id' | 'type'> {
  label: ReactNode
  error?: string | undefined
  hint?: string | undefined
}

export function Checkbox({ label, error, hint, className = '', ...props }: CheckboxProps) {
  const id = useId()
  const errorId = `${id}-error`

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-start gap-2.5">
        <input
          id={id}
          type="checkbox"
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : undefined}
          className={`mt-0.5 size-4.5 shrink-0 rounded-sm border-line text-interactive accent-[var(--color-interactive)] ${className}`}
          {...props}
        />

        <label htmlFor={id} className="text-[15px] leading-snug text-ink">
          {label}
        </label>
      </div>

      {error ? (
        <p id={errorId} role="alert" className="text-sm text-accent">
          {error}
        </p>
      ) : hint ? (
        <p className="text-sm text-muted">{hint}</p>
      ) : null}
    </div>
  )
}

export interface CheckboxOption {
  value: string
  label: string
}

interface CheckboxGroupProps {
  label: string
  options: CheckboxOption[]
  /** Selected values. The order the user picks them in is preserved. */
  value: string[]
  onChange: (value: string[]) => void
  error?: string | undefined
  hint?: string | undefined
  required?: boolean
}

/** Renders a MultiSelect attribute. The submitted value stays a list of option values. */
export function CheckboxGroup({
  label,
  options,
  value,
  onChange,
  error,
  hint,
  required = false,
}: CheckboxGroupProps) {
  const groupId = useId()

  function toggle(option: string, checked: boolean) {
    onChange(checked ? [...value, option] : value.filter((v) => v !== option))
  }

  return (
    <fieldset className="flex flex-col gap-2" aria-describedby={error ? `${groupId}-error` : undefined}>
      <legend className="text-sm font-medium text-ink">
        {label}
        {required ? <span className="ml-0.5 text-accent">*</span> : null}
      </legend>

      <div className="grid gap-1.5 sm:grid-cols-2">
        {options.map((option) => (
          <Checkbox
            key={option.value}
            label={option.label}
            checked={value.includes(option.value)}
            onChange={(event) => toggle(option.value, event.target.checked)}
          />
        ))}
      </div>

      {error ? (
        <p id={`${groupId}-error`} role="alert" className="text-sm text-accent">
          {error}
        </p>
      ) : hint ? (
        <p className="text-sm text-muted">{hint}</p>
      ) : null}
    </fieldset>
  )
}
