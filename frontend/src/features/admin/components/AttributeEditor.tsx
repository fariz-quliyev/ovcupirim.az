import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'

import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Checkbox } from '@/components/ui/Checkbox'
import { ErrorState } from '@/components/ui/ErrorState'
import { Input } from '@/components/ui/Input'
import { Select } from '@/components/ui/Select'
import { Skeleton } from '@/components/ui/Skeleton'

import {
  adminKeys,
  createAttribute,
  createAttributeOption,
  deleteAttribute,
  deleteAttributeOption,
  getCategoryAttributes,
} from '../api'
import type { AdminAttribute } from '../types'
import { useAdminAction } from '../useAdminAction'
import { ConfirmDialog } from './ActionDialog'

/** The data types the schema supports. Rendered from the server's own vocabulary, not invented. */
const dataTypes = ['Text', 'Number', 'Select', 'MultiSelect', 'Boolean'] as const

const optionTypes = new Set(['Select', 'MultiSelect'])

interface AttributeEditorProps {
  categoryId: number
  /** Attributes hang off leaves; a parent shows why the editor is not offered. */
  isLeaf: boolean
}

/**
 * Attribute and option administration for one category.
 *
 * Everything here is a thin call onto the existing taxonomy endpoints — which key is valid, what a
 * data type means and whether an attribute may be removed are all decided server-side. Deletions
 * confirm first, because an attribute in use takes its listings' values with it.
 */
export function AttributeEditor({ categoryId, isLeaf }: AttributeEditorProps) {
  const [adding, setAdding] = useState(false)
  const [removing, setRemoving] = useState<AdminAttribute | null>(null)
  const [optionFor, setOptionFor] = useState<AdminAttribute | null>(null)

  const attributes = useQuery({
    queryKey: adminKeys.categoryAttributes(categoryId),
    queryFn: () => getCategoryAttributes(categoryId),
  })

  const invalidate = [adminKeys.categoryAttributes(categoryId), adminKeys.categories]

  const create = useAdminAction<{ key: string; labelAz: string; dataType: string; unit: string | null; isRequired: boolean; isFilterable: boolean; sortOrder: number }>({
    action: (body) => createAttribute({ ...body, categoryId }),
    invalidate,
    onSuccess: () => setAdding(false),
  })

  const remove = useAdminAction<number>({
    action: (id) => deleteAttribute(id),
    invalidate,
    onSuccess: () => setRemoving(null),
  })

  const addOption = useAdminAction<{ attributeId: number; value: string; labelAz: string; sortOrder: number }>({
    action: ({ attributeId, ...body }) => createAttributeOption(attributeId, body),
    invalidate,
    onSuccess: () => setOptionFor(null),
  })

  const removeOption = useAdminAction<{ attributeId: number; optionId: number }>({
    action: ({ attributeId, optionId }) => deleteAttributeOption(attributeId, optionId),
    invalidate,
  })

  if (!isLeaf) {
    return (
      <p className="text-sm text-muted">
        Xüsusiyyətlər yalnız alt kateqoriyalarda təyin olunur.
      </p>
    )
  }

  if (attributes.isPending) {
    return <Skeleton className="h-24" />
  }

  if (attributes.isError || !attributes.data) {
    return (
      <ErrorState
        description="Xüsusiyyətləri yükləmək mümkün olmadı."
        onRetry={() => void attributes.refetch()}
      />
    )
  }

  const failure = [create, remove, addOption, removeOption].find((a) => a.state.kind === 'error')

  return (
    <div className="flex flex-col gap-3">
      {failure?.state.kind === 'error' ? (
        <p role="alert" className="text-sm text-accent">
          {failure.state.message}
        </p>
      ) : null}

      {attributes.data.length === 0 ? (
        <p className="text-sm text-muted">Bu kateqoriyada xüsusiyyət yoxdur.</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {attributes.data.map((attribute) => (
            <li key={attribute.id} className="rounded-(--radius-input) border border-line p-3">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div className="flex flex-col gap-0.5">
                  <span className="text-sm font-medium text-ink">
                    {attribute.labelAz} <span className="text-faint">({attribute.key})</span>
                  </span>
                  <span className="flex flex-wrap gap-1 text-xs text-muted">
                    <Badge>{attribute.dataType}</Badge>
                    {attribute.unit ? <Badge>{attribute.unit}</Badge> : null}
                    {attribute.isRequired ? <Badge tone="top">Məcburi</Badge> : null}
                    {attribute.isFilterable ? <Badge tone="neutral">Filtr</Badge> : null}
                    {!attribute.isActive ? <Badge>Deaktiv</Badge> : null}
                  </span>
                </div>

                <div className="flex gap-1.5">
                  {optionTypes.has(attribute.dataType) ? (
                    <Button type="button" size="sm" variant="secondary" onClick={() => setOptionFor(attribute)}>
                      Dəyər əlavə et
                    </Button>
                  ) : null}

                  <Button type="button" size="sm" variant="ghost" onClick={() => setRemoving(attribute)}>
                    Sil
                  </Button>
                </div>
              </div>

              {attribute.options.length > 0 ? (
                <ul className="mt-2 flex flex-wrap gap-1.5">
                  {attribute.options.map((option) => (
                    <li
                      key={option.id}
                      className="flex items-center gap-1 rounded-full border border-line px-2 py-0.5 text-xs"
                    >
                      <span className="text-ink">{option.labelAz}</span>
                      <span className="text-faint">{option.value}</span>
                      <button
                        type="button"
                        aria-label={`${option.labelAz} dəyərini sil`}
                        className="text-accent"
                        onClick={() =>
                          removeOption.run({ attributeId: attribute.id, optionId: option.id })
                        }
                      >
                        ×
                      </button>
                    </li>
                  ))}
                </ul>
              ) : null}
            </li>
          ))}
        </ul>
      )}

      <div>
        <Button type="button" size="sm" variant="secondary" onClick={() => setAdding(true)}>
          Xüsusiyyət əlavə et
        </Button>
      </div>

      {adding ? (
        <form
          className="flex flex-col gap-3 rounded-(--radius-input) border border-line p-3"
          onSubmit={(event) => {
            event.preventDefault()
            const form = new FormData(event.currentTarget)

            create.run({
              key: String(form.get('key') ?? '').trim(),
              labelAz: String(form.get('labelAz') ?? '').trim(),
              dataType: String(form.get('dataType') ?? 'Text'),
              unit: String(form.get('unit') ?? '').trim() || null,
              isRequired: form.get('isRequired') === 'on',
              isFilterable: form.get('isFilterable') === 'on',
              sortOrder: Number(form.get('sortOrder') ?? 0) || 0,
            })
          }}
        >
          <Input label="Açar *" name="key" required placeholder="capacity_person" />
          <Input label="Etiket (AZ) *" name="labelAz" required />

          <Select label="Növ" name="dataType" defaultValue="Text">
            {dataTypes.map((type) => (
              <option key={type} value={type}>
                {type}
              </option>
            ))}
          </Select>

          <Input label="Vahid" name="unit" placeholder="kq" />
          <Input label="Sıra" name="sortOrder" type="number" defaultValue="0" />

          <Checkbox label="Məcburi" name="isRequired" />
          <Checkbox label="Filtrdə göstərilsin" name="isFilterable" />

          <div className="flex gap-2">
            <Button type="submit" size="sm" disabled={create.isPending}>
              Əlavə et
            </Button>
            <Button type="button" size="sm" variant="secondary" onClick={() => setAdding(false)}>
              İmtina
            </Button>
          </div>
        </form>
      ) : null}

      {optionFor ? (
        <form
          className="flex flex-col gap-3 rounded-(--radius-input) border border-line p-3"
          onSubmit={(event) => {
            event.preventDefault()
            const form = new FormData(event.currentTarget)

            addOption.run({
              attributeId: optionFor.id,
              value: String(form.get('value') ?? '').trim(),
              labelAz: String(form.get('labelAz') ?? '').trim(),
              sortOrder: Number(form.get('sortOrder') ?? 0) || 0,
            })
          }}
        >
          <p className="text-sm text-muted">“{optionFor.labelAz}” üçün yeni dəyər</p>

          <Input label="Dəyər *" name="value" required placeholder="2" />
          <Input label="Etiket (AZ) *" name="labelAz" required placeholder="2 nəfər" />
          <Input label="Sıra" name="sortOrder" type="number" defaultValue="0" />

          <div className="flex gap-2">
            <Button type="submit" size="sm" disabled={addOption.isPending}>
              Əlavə et
            </Button>
            <Button type="button" size="sm" variant="secondary" onClick={() => setOptionFor(null)}>
              İmtina
            </Button>
          </div>
        </form>
      ) : null}

      {removing ? (
        <ConfirmDialog
          title="Xüsusiyyəti sil"
          description={`“${removing.labelAz}” silinir. Bu xüsusiyyətdən istifadə edən elanlardakı dəyərlər artıq göstərilməyəcək. Əməliyyat geri qaytarılmır.`}
          confirmLabel="Sil"
          busy={remove.isPending}
          onConfirm={() => remove.run(removing.id)}
          onCancel={() => setRemoving(null)}
        />
      ) : null}
    </div>
  )
}
