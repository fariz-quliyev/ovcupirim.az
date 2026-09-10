import { useState } from 'react'

import { Button } from '@/components/ui/Button'
import { Checkbox } from '@/components/ui/Checkbox'
import { Input } from '@/components/ui/Input'
import { Textarea } from '@/components/ui/Textarea'

import type { CategoryEditBody } from '../api'
import type { AdminCategoryNode } from '../types'

interface CategoryEditorProps {
  category: AdminCategoryNode
  busy: boolean
  error: string | undefined
  onSave: (body: CategoryEditBody) => void
}

/**
 * Edits a category.
 *
 * Every field of `UpdateCategoryRequest` is present, including the descriptive and SEO ones an
 * operator rarely touches: the server assigns all of them, so a form that showed only some would
 * blank the rest on the first save. That is why the admin tree carries them back.
 *
 * The caller keys this component on the category id, so selecting a different one remounts it with
 * fresh initial state rather than resynchronising through an effect.
 */
export function CategoryEditor({ category, busy, error, onSave }: CategoryEditorProps) {
  const [form, setForm] = useState<CategoryEditBody>(() => toBody(category))

  function set<K extends keyof CategoryEditBody>(key: K, value: CategoryEditBody[K]) {
    setForm((current) => ({ ...current, [key]: value }))
  }

  return (
    <form
      className="flex flex-col gap-3"
      onSubmit={(event) => {
        event.preventDefault()
        onSave(form)
      }}
      noValidate
    >
      <Input
        label="Ad (AZ) *"
        value={form.nameAz}
        maxLength={100}
        onChange={(event) => set('nameAz', event.target.value)}
        required
      />

      <Input
        label="Ad (RU)"
        value={form.nameRu ?? ''}
        maxLength={100}
        onChange={(event) => set('nameRu', blankToNull(event.target.value))}
      />

      <Textarea
        label="Təsvir"
        value={form.descriptionAz ?? ''}
        rows={3}
        maxLength={1000}
        onChange={(event) => set('descriptionAz', blankToNull(event.target.value))}
      />

      <Input
        label="Meta başlıq"
        value={form.metaTitleAz ?? ''}
        maxLength={200}
        onChange={(event) => set('metaTitleAz', blankToNull(event.target.value))}
      />

      <Textarea
        label="Meta təsvir"
        value={form.metaDescriptionAz ?? ''}
        rows={2}
        maxLength={300}
        onChange={(event) => set('metaDescriptionAz', blankToNull(event.target.value))}
      />

      <div className="grid gap-3 sm:grid-cols-2">
        <Input
          label="İkon açarı"
          value={form.iconKey ?? ''}
          maxLength={60}
          onChange={(event) => set('iconKey', blankToNull(event.target.value))}
        />

        <Input
          label="Şəkil açarı"
          value={form.imageKey ?? ''}
          maxLength={200}
          onChange={(event) => set('imageKey', blankToNull(event.target.value))}
        />
      </div>

      <Input
        label="Sıra"
        type="number"
        value={String(form.sortOrder)}
        onChange={(event) => set('sortOrder', Number(event.target.value) || 0)}
      />

      <Checkbox
        label="Aktiv"
        checked={form.isActive}
        onChange={(event) => set('isActive', event.target.checked)}
        hint="Deaktiv kateqoriya saytda görünmür."
      />

      <Checkbox
        label="Elan yerləşdirmək üçün seçilə bilər"
        checked={form.isSelectable}
        onChange={(event) => set('isSelectable', event.target.checked)}
      />

      {error ? (
        <p role="alert" className="text-sm text-accent">
          {error}
        </p>
      ) : null}

      <div>
        <Button type="submit" size="sm" disabled={busy || form.nameAz.trim() === ''}>
          {busy ? 'Saxlanılır…' : 'Yadda saxla'}
        </Button>
      </div>
    </form>
  )
}

/** The complete request shape, read straight off the category so nothing is dropped. */
function toBody(category: AdminCategoryNode): CategoryEditBody {
  return {
    nameAz: category.nameAz,
    nameRu: category.nameRu,
    descriptionAz: category.descriptionAz,
    metaTitleAz: category.metaTitleAz,
    metaDescriptionAz: category.metaDescriptionAz,
    iconKey: category.iconKey,
    imageKey: category.imageKey,
    sortOrder: category.sortOrder,
    isActive: category.isActive,
    isSelectable: category.isSelectable,
  }
}

function blankToNull(value: string): string | null {
  return value.trim() === '' ? null : value
}
