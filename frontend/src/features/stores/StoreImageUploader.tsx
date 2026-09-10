import { useRef, useState } from 'react'

import { ApiError } from '@/api/client'
import { Button } from '@/components/ui/Button'
import { ACCEPTED, MAX_BYTES } from '@/features/listings/ImageUploader'

import { removeStoreImage, uploadStoreImage } from './api'
import type { StoreOwner } from './types'

interface StoreImageUploaderProps {
  kind: 'logo' | 'banner'
  label: string
  hint: string
  url: string | null
  onChange: (store: StoreOwner) => void
}

/**
 * One picture, replaced in place. Same accepted types and size ceiling as listing images, because
 * it is the same server-side pipeline: magic-byte check, decode bounds, EXIF stripped, re-encoded.
 */
export function StoreImageUploader({ kind, label, hint, url, onChange }: StoreImageUploaderProps) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleFile(files: FileList | null) {
    const file = files?.[0]

    if (!file) {
      return
    }

    setError(null)

    if (file.size > MAX_BYTES) {
      setError('Şəkil 5 MB-dan böyükdür.')
      return
    }

    setBusy(true)

    try {
      onChange(await uploadStoreImage(kind, file))
    } catch (caught) {
      setError(
        caught instanceof ApiError
          ? (caught.fieldError('file') ?? caught.message)
          : 'Şəkli yükləmək mümkün olmadı.',
      )
    } finally {
      setBusy(false)
      if (inputRef.current) {
        inputRef.current.value = ''
      }
    }
  }

  async function remove() {
    setBusy(true)
    setError(null)

    try {
      onChange(await removeStoreImage(kind))
    } catch {
      setError('Şəkli silmək mümkün olmadı.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex flex-col gap-2">
      <p className="text-sm font-medium text-ink">{label}</p>

      {url ? (
        <img
          src={url}
          alt=""
          className={`rounded-(--radius-input) border border-line bg-canvas object-cover ${
            kind === 'logo' ? 'size-24' : 'h-28 w-full'
          }`}
        />
      ) : (
        <p className="rounded-(--radius-card) border border-dashed border-line px-4 py-5 text-center text-sm text-muted">
          {hint}
        </p>
      )}

      <input
        ref={inputRef}
        type="file"
        accept={ACCEPTED}
        className="hidden"
        aria-label={`${label} seçin`}
        onChange={(event) => void handleFile(event.target.files)}
      />

      <div className="flex flex-wrap gap-2">
        <Button
          type="button"
          variant="secondary"
          size="sm"
          disabled={busy}
          onClick={() => inputRef.current?.click()}
        >
          {url ? 'Dəyiş' : 'Yüklə'}
        </Button>

        {url ? (
          <Button type="button" variant="ghost" size="sm" disabled={busy} onClick={() => void remove()}>
            Sil
          </Button>
        ) : null}
      </div>

      {error ? (
        <p role="alert" className="text-sm text-accent">
          {error}
        </p>
      ) : null}
    </div>
  )
}
