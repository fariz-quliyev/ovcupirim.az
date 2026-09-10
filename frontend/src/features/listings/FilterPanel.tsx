import { Button } from '@/components/ui/Button'
import { Checkbox } from '@/components/ui/Checkbox'
import { Input } from '@/components/ui/Input'
import { Select } from '@/components/ui/Select'
import { orderedAttributes } from '@/features/catalog/attributeControls'
import { RegionSelect } from '@/features/catalog/RegionSelect'
import type { CategorySchema } from '@/features/catalog/types'

import { AttributeFilter } from './AttributeFilter'
import type { FacetItem } from './types'

interface FilterPanelProps {
  /** Absent outside a category, which is when dynamic attribute filters do not apply. */
  schema: CategorySchema | undefined
  values: Record<string, string>
  onChange: (next: Record<string, string | null>) => void
  onReset: () => void
  categoryFacets: FacetItem[]
  onPickCategory: (slug: string | null) => void
}

/**
 * The filter rail. Price, place and the fixed listing fields are always available; the dynamic
 * attribute filters appear only once a category is chosen, because only a category has a schema to
 * validate them against — the same rule the server applies.
 */
export function FilterPanel({
  schema,
  values,
  onChange,
  onReset,
  categoryFacets,
  onPickCategory,
}: FilterPanelProps) {
  const attributes = schema ? orderedAttributes(schema.attributes) : []

  return (
    <aside className="flex flex-col gap-5 rounded-(--radius-card) border border-line bg-surface p-4">
      <div className="flex items-center justify-between gap-2">
        <h2 className="text-base font-semibold text-ink">Filtrlər</h2>
        <Button type="button" variant="ghost" size="sm" onClick={onReset}>
          Sıfırla
        </Button>
      </div>

      {categoryFacets.length > 0 ? (
        <section className="flex flex-col gap-1.5">
          <h3 className="text-sm font-medium text-ink">Kateqoriya</h3>

          {schema ? (
            <button
              type="button"
              onClick={() => onPickCategory(null)}
              className="text-left text-sm text-interactive hover:underline"
            >
              ← Bütün kateqoriyalar
            </button>
          ) : null}

          <ul className="flex flex-col gap-1">
            {categoryFacets.slice(0, 12).map((facet) => (
              <li key={facet.slug}>
                <button
                  type="button"
                  onClick={() => onPickCategory(facet.slug)}
                  aria-current={values['category'] === facet.slug ? 'true' : undefined}
                  className={`flex w-full items-baseline justify-between gap-2 text-left text-sm ${
                    values['category'] === facet.slug ? 'font-semibold text-ink' : 'text-muted hover:text-ink'
                  }`}
                >
                  <span>{facet.nameAz}</span>
                  <span className="text-xs tabular-nums text-faint">{facet.count}</span>
                </button>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      <fieldset className="flex flex-col gap-2">
        <legend className="text-sm font-medium text-ink">Qiymət, ₼</legend>

        <div className="grid grid-cols-2 gap-2">
          <Input
            type="number"
            inputMode="decimal"
            min={0}
            label="min."
            value={values['priceMin'] ?? ''}
            onChange={(event) => onChange({ priceMin: event.target.value || null })}
          />
          <Input
            type="number"
            inputMode="decimal"
            min={0}
            label="maks."
            value={values['priceMax'] ?? ''}
            onChange={(event) => onChange({ priceMax: event.target.value || null })}
          />
        </div>
      </fieldset>

      <RegionSelect
        value={values['region'] ?? ''}
        onChange={(slug) => onChange({ region: slug || null })}
      />

      <Select
        label="Vəziyyət"
        value={values['condition'] ?? ''}
        onChange={(event) => onChange({ condition: event.target.value || null })}
      >
        <option value="">Vacib deyil</option>
        <option value="New">Yeni</option>
        <option value="Used">İşlənmiş</option>
      </Select>

      <Checkbox
        label="Yalnız çatdırılma ilə"
        checked={values['delivery'] === 'true'}
        onChange={(event) => onChange({ delivery: event.target.checked ? 'true' : null })}
      />

      {attributes.length > 0 ? (
        <section className="flex flex-col gap-4 border-t border-line pt-4">
          {attributes.map((attribute) => (
            <AttributeFilter
              key={attribute.key}
              attribute={attribute}
              values={values}
              onChange={onChange}
            />
          ))}
        </section>
      ) : null}
    </aside>
  )
}
