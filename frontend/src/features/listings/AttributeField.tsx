import { Checkbox, CheckboxGroup } from '@/components/ui/Checkbox'
import { Input } from '@/components/ui/Input'
import { Select } from '@/components/ui/Select'
import { controlForAttribute } from '@/features/catalog/attributeControls'
import type { AttributeSchema } from '@/features/catalog/types'

interface AttributeFieldProps {
  attribute: AttributeSchema
  value: unknown
  onChange: (key: string, value: unknown) => void
  error?: string | undefined
}

/**
 * Renders one schema field. Everything on screen — label, placeholder, help text, unit, bounds and
 * options — comes from the schema, and the control is chosen from `dataType` alone. No attribute
 * key or category name appears anywhere in this file, which is what keeps category-specific
 * behaviour out of React.
 */
export function AttributeField({ attribute, value, onChange, error }: AttributeFieldProps) {
  const label = attribute.unit ? `${attribute.labelAz}, ${attribute.unit}` : attribute.labelAz
  const required = attribute.isRequired
  const hint = attribute.helpTextAz ?? undefined

  switch (controlForAttribute(attribute)) {
    case 'text':
      return (
        <Input
          label={labelWithMarker(label, required)}
          value={typeof value === 'string' ? value : ''}
          onChange={(event) => onChange(attribute.key, event.target.value)}
          placeholder={attribute.placeholderAz ?? undefined}
          maxLength={attribute.maxLength ?? undefined}
          required={required}
          error={error}
          hint={hint}
        />
      )

    case 'number':
      return (
        <Input
          type="number"
          inputMode="decimal"
          label={labelWithMarker(label, required)}
          value={typeof value === 'number' || typeof value === 'string' ? String(value) : ''}
          onChange={(event) =>
            onChange(attribute.key, event.target.value === '' ? undefined : event.target.value)
          }
          placeholder={attribute.placeholderAz ?? undefined}
          min={attribute.minValue ?? undefined}
          max={attribute.maxValue ?? undefined}
          step={stepFor(attribute.decimalPlaces)}
          required={required}
          error={error}
          hint={hint}
        />
      )

    case 'checkbox':
      return (
        <Checkbox
          label={labelWithMarker(label, required)}
          checked={value === true}
          onChange={(event) => onChange(attribute.key, event.target.checked)}
          error={error}
          hint={hint}
        />
      )

    case 'dropdown':
      return (
        <Select
          label={labelWithMarker(label, required)}
          value={typeof value === 'string' ? value : ''}
          onChange={(event) =>
            onChange(attribute.key, event.target.value === '' ? undefined : event.target.value)
          }
          placeholder="Seçin"
          required={required}
          error={error}
          hint={hint}
        >
          {(attribute.options ?? []).map((option) => (
            <option key={option.value} value={option.value}>
              {option.labelAz}
            </option>
          ))}
        </Select>
      )

    case 'checkbox-group':
      return (
        <CheckboxGroup
          label={label}
          required={required}
          options={(attribute.options ?? []).map((option) => ({
            value: option.value,
            label: option.labelAz,
          }))}
          value={Array.isArray(value) ? (value as string[]) : []}
          onChange={(next) => onChange(attribute.key, next.length === 0 ? undefined : next)}
          error={error}
          hint={hint}
        />
      )
  }
}

function labelWithMarker(label: string, required: boolean): string {
  return required ? `${label} *` : label
}

/** A field with two decimals accepts 0.01 steps; an integer field steps by one. */
function stepFor(decimalPlaces: number | null): string {
  if (decimalPlaces === null || decimalPlaces <= 0) {
    return '1'
  }

  return `0.${'0'.repeat(decimalPlaces - 1)}1`
}
