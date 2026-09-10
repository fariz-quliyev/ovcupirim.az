import { Checkbox, CheckboxGroup } from '@/components/ui/Checkbox'
import { Input } from '@/components/ui/Input'
import { Select } from '@/components/ui/Select'
import { filterForAttribute, filterParamNames } from '@/features/catalog/attributeControls'
import type { AttributeSchema } from '@/features/catalog/types'

interface AttributeFilterProps {
  attribute: AttributeSchema
  /** Current values, keyed by the query-string names this attribute contributes. */
  values: Record<string, string>
  onChange: (next: Record<string, string | null>) => void
}

/**
 * One filter control, chosen from the schema alone — the mirror of the listing form's
 * AttributeField. A Number becomes a range, a Boolean a single toggle, and both option types a
 * multi-choice group, because filtering by "either of these" is what a buyer wants even when the
 * seller could only pick one. No attribute key or category name appears in this file.
 */
export function AttributeFilter({ attribute, values, onChange }: AttributeFilterProps) {
  const kind = filterForAttribute(attribute)
  const names = filterParamNames(attribute)
  const label = attribute.unit ? `${attribute.labelAz}, ${attribute.unit}` : attribute.labelAz

  if (kind === 'none') {
    return null
  }

  if (kind === 'range') {
    const [minName, maxName] = names as [string, string]

    return (
      <fieldset className="flex flex-col gap-2">
        <legend className="text-sm font-medium text-ink">{label}</legend>

        <div className="grid grid-cols-2 gap-2">
          <Input
            type="number"
            inputMode="decimal"
            label="min."
            value={values[minName] ?? ''}
            min={attribute.minValue ?? undefined}
            max={attribute.maxValue ?? undefined}
            onChange={(event) => onChange({ [minName]: event.target.value || null })}
          />
          <Input
            type="number"
            inputMode="decimal"
            label="maks."
            value={values[maxName] ?? ''}
            min={attribute.minValue ?? undefined}
            max={attribute.maxValue ?? undefined}
            onChange={(event) => onChange({ [maxName]: event.target.value || null })}
          />
        </div>
      </fieldset>
    )
  }

  const name = names[0]!
  const current = values[name] ?? ''

  if (kind === 'toggle') {
    return (
      <Checkbox
        label={label}
        checked={current === 'true'}
        onChange={(event) => onChange({ [name]: event.target.checked ? 'true' : null })}
      />
    )
  }

  const options = (attribute.options ?? []).map((option) => ({
    value: option.value,
    label: option.labelAz,
  }))

  // A short list reads better as checkboxes; a long one would swamp the panel, so it collapses
  // into a single-choice dropdown.
  if (options.length > 8) {
    return (
      <Select
        label={label}
        value={current}
        onChange={(event) => onChange({ [name]: event.target.value || null })}
      >
        <option value="">Hamısı</option>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </Select>
    )
  }

  const selected = current === '' ? [] : current.split(',')

  return (
    <CheckboxGroup
      label={label}
      options={options}
      value={selected}
      onChange={(next) => onChange({ [name]: next.length === 0 ? null : next.join(',') })}
    />
  )
}
