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

  /** Resolves to the key the server assigned, or null once the picture is removed. */
  onUploadImage: (file: File) => Promise<string | null>
  onRemoveImage: () => Promise<void>
}

/**
 * Resolves an image key the way the public homepage does, so the preview here shows exactly what a
 * visitor will get. Storage:Local:PublicBaseUrl is a relative `/uploads` in every environment; an
 * absolute URL is passed through untouched.
 */
function categoryImagePreview(imageKey: string): string {
  return /^https?:\/\//i.test(imageKey) ? imageKey : `/uploads/${imageKey.replace(/^\/+/, '')}`
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
export function CategoryEditor({
  category,
  busy,
  error,
  onSave,
  onUploadImage,
  onRemoveImage,
}: CategoryEditorProps) {
  const [form, setForm] = useState<CategoryEditBody>(() => toBody(category))
  const [uploading, setUploading] = useState(false)
  const [imageError, setImageError] = useState<string | null>(null)

  function set<K extends keyof CategoryEditBody>(key: K, value: CategoryEditBody[K]) {
    setForm((current) => ({ ...current, [key]: value }))
  }

  // The new key goes straight into the form. The tree refetches too, but this component is keyed on
  // the category id and so is not remounted by that — without this the field would still hold the
  // old key and the next save would put it back.
  async function upload(file: File) {
    setUploading(true)
    setImageError(null)

    try {
      set('imageKey', await onUploadImage(file))
    } catch (caught) {
      setImageError(caught instanceof Error ? caught.message : 'Şəkli yükləmək mümkün olmadı.')
    } finally {
      setUploading(false)
    }
  }

  async function remove() {
    setUploading(true)
    setImageError(null)

    try {
      await onRemoveImage()
      set('imageKey', null)
    } catch (caught) {
      setImageError(caught instanceof Error ? caught.message : 'Şəkli silmək mümkün olmadı.')
    } finally {
      setUploading(false)
    }
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

        <div className="flex flex-col gap-2">
          <Input
            label="Şəkil açarı"
            value={form.imageKey ?? ''}
            maxLength={200}
            onChange={(event) => set('imageKey', blankToNull(event.target.value))}
            hint="Ana səhifədəki kateqoriya kafelində görünür. Adətən aşağıdakı düymə ilə yüklənir; sahəyə əl ilə açar və ya tam ünvan da yazmaq olar. Boş qalsa, kafeldə ikon göstərilir."
          />

          <div className="flex items-start gap-3">
            {/* A preview, so a mistyped key is visible here instead of on the homepage. */}
            {form.imageKey ? (
              <img
                src={categoryImagePreview(form.imageKey)}
                alt=""
                className="size-20 shrink-0 rounded-(--radius-button) border border-line object-cover"
              />
            ) : null}

            <div className="flex flex-col items-start gap-1.5">
              {/* The upload saves the picture on its own, separately from the form's save button:
                  it has to reach the server to be given a key, and that key is what the field then
                  holds. Pressing it does not commit the rest of the form. */}
              <label className="inline-flex cursor-pointer items-center rounded-(--radius-button) border border-line px-3 py-2 text-sm font-semibold text-interactive hover:bg-canvas">
                {uploading ? 'Yüklənir…' : 'Şəkil yüklə'}
                <input
                  type="file"
                  accept="image/jpeg,image/png,image/webp"
                  className="sr-only"
                  disabled={uploading || busy}
                  onChange={(event) => {
                    const file = event.target.files?.[0]
                    event.target.value = ''

                    if (file) {
                      void upload(file)
                    }
                  }}
                />
              </label>

              {form.imageKey ? (
                <button
                  type="button"
                  disabled={uploading || busy}
                  onClick={() => void remove()}
                  className="text-sm text-muted hover:text-ink disabled:opacity-50"
                >
                  Şəkli sil
                </button>
              ) : null}

              <p className="text-xs text-muted">JPEG, PNG və ya WebP. Ən çox 5 MB.</p>

              {imageError ? (
                <p role="alert" className="text-sm text-accent">
                  {imageError}
                </p>
              ) : null}
            </div>
          </div>
        </div>
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
