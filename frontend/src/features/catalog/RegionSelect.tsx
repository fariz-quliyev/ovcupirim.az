import { useId } from 'react'

import { useRegions } from './hooks'

interface RegionSelectProps {
  value: string
  onChange: (slug: string) => void
  label?: string
  /**
   * Listing creation requires a location and offers no empty choice; the filter panel allows
   * "all regions". The server enforces the rule either way.
   */
  required?: boolean
}

/**
 * One flat native select. No grouping and no client-side sorting — the server returns the list in
 * the order it should be shown, pinned cities first.
 */
export function RegionSelect({
  value,
  onChange,
  label = 'Şəhər / rayon',
  required = false,
}: RegionSelectProps) {
  const id = useId()
  const { data, isPending, isError } = useRegions()

  const regions = data ?? []
  const isEmpty = !isPending && !isError && regions.length === 0

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-medium text-ink">
        {label}
      </label>

      <select
        id={id}
        value={value}
        required={required}
        aria-invalid={required && value === '' ? true : undefined}
        disabled={isPending || isError || isEmpty}
        onChange={(event) => onChange(event.target.value)}
        className="h-11 rounded-(--radius-input) border border-line bg-surface px-3 text-[15px] outline-none focus:border-interactive disabled:bg-canvas disabled:text-faint"
      >
        {required ? (
          <option value="" disabled>
            Seçin
          </option>
        ) : (
          <option value="">Bütün regionlar</option>
        )}

        {regions.map((region) => (
          <option key={region.slug} value={region.slug}>
            {region.nameAz}
          </option>
        ))}
      </select>

      {isError ? (
        <p role="alert" className="text-sm text-accent">
          Regionları yükləmək mümkün olmadı.
        </p>
      ) : null}

      {/* The real state until the authoritative dataset is imported. */}
      {isEmpty ? <p className="text-sm text-muted">Region siyahısı hələ yüklənməyib.</p> : null}
    </div>
  )
}
