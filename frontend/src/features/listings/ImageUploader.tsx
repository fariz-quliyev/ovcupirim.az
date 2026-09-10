import { useRef, useState } from 'react'

import { ApiError } from '@/api/client'
import { Button } from '@/components/ui/Button'

import { deleteListingMedia, reorderListingMedia, uploadListingMedia } from './api'
import type { ListingMedia } from './types'

/** Approved Phase 0 limits. The server enforces the same numbers; these only shape the UI. */
export const MAX_IMAGES = 10
export const MAX_BYTES = 5 * 1024 * 1024
// B-3: HEIC/HEIF is off the accepted contract. This only steers the file picker's own filter — the
// server is what actually enforces it, and sniffs the bytes regardless of what a file claims to be.
export const ACCEPTED = 'image/jpeg,image/png,image/webp'

interface ImageUploaderProps {
  listingId: string
  media: ListingMedia[]
  onChange: (media: ListingMedia[]) => void
  disabled?: boolean
}

/**
 * Upload, remove and reorder. The first image is the cover, which is how a card and the Open Graph
 * preview pick their picture.
 */
export function ImageUploader({ listingId, media, onChange, disabled = false }: ImageUploaderProps) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const remaining = MAX_IMAGES - media.length

  async function handleFiles(files: FileList | null) {
    if (!files || files.length === 0) {
      return
    }

    setError(null)
    setBusy(true)

    const uploaded: ListingMedia[] = []

    try {
      for (const file of Array.from(files).slice(0, remaining)) {
        if (file.size > MAX_BYTES) {
          setError(`"${file.name}" 5 MB-dan böyükdür.`)
          continue
        }

        uploaded.push(await uploadListingMedia(listingId, file))
      }

      if (uploaded.length > 0) {
        onChange([...media, ...uploaded])
      }
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

  async function remove(mediaId: string) {
    setBusy(true)
    setError(null)

    try {
      await deleteListingMedia(listingId, mediaId)
      onChange(media.filter((m) => m.id !== mediaId))
    } catch {
      setError('Şəkli silmək mümkün olmadı.')
    } finally {
      setBusy(false)
    }
  }

  async function move(mediaId: string, direction: -1 | 1) {
    const from = media.findIndex((m) => m.id === mediaId)
    const to = from + direction

    if (from < 0 || to < 0 || to >= media.length) {
      return
    }

    const next = [...media]
    const [moved] = next.splice(from, 1)
    next.splice(to, 0, moved!)

    setBusy(true)
    setError(null)

    try {
      onChange(await reorderListingMedia(listingId, next.map((m) => m.id)))
    } catch {
      setError('Sıralamanı yadda saxlamaq mümkün olmadı.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="secondary"
          onClick={() => inputRef.current?.click()}
          disabled={disabled || busy || remaining <= 0}
        >
          Şəkil əlavə et
        </Button>

        <span className="text-sm text-muted">
          {media.length} / {MAX_IMAGES} · hər şəkil ən çox 5 MB
        </span>
      </div>

      <input
        ref={inputRef}
        type="file"
        accept={ACCEPTED}
        multiple
        className="hidden"
        aria-label="Şəkil seçin"
        onChange={(event) => void handleFiles(event.target.files)}
      />

      {error ? (
        <p role="alert" className="text-sm text-accent">
          {error}
        </p>
      ) : null}

      {media.length === 0 ? (
        <p className="rounded-(--radius-card) border border-dashed border-line px-4 py-6 text-center text-sm text-muted">
          Elan üçün ən azı bir şəkil tələb olunur.
        </p>
      ) : (
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          {media.map((image, index) => (
            <li
              key={image.id}
              className="relative overflow-hidden rounded-(--radius-card) border border-line bg-surface"
            >
              <img
                src={image.variants['card'] ?? image.url}
                alt=""
                width={image.width}
                height={image.height}
                className="aspect-4/3 w-full object-cover"
              />

              {image.isPrimary ? (
                <span className="absolute left-1.5 top-1.5 rounded-full bg-interactive px-2 py-0.5 text-xs font-semibold text-white">
                  Əsas şəkil
                </span>
              ) : null}

              <div className="flex items-center justify-between gap-1 p-1.5">
                <div className="flex gap-1">
                  <button
                    type="button"
                    aria-label="Əvvələ keçir"
                    disabled={busy || index === 0}
                    onClick={() => void move(image.id, -1)}
                    className="rounded-sm px-1.5 py-0.5 text-sm text-muted hover:text-ink disabled:opacity-40"
                  >
                    ←
                  </button>
                  <button
                    type="button"
                    aria-label="Sonraya keçir"
                    disabled={busy || index === media.length - 1}
                    onClick={() => void move(image.id, 1)}
                    className="rounded-sm px-1.5 py-0.5 text-sm text-muted hover:text-ink disabled:opacity-40"
                  >
                    →
                  </button>
                </div>

                <button
                  type="button"
                  disabled={busy}
                  onClick={() => void remove(image.id)}
                  className="rounded-sm px-1.5 py-0.5 text-sm text-accent hover:underline disabled:opacity-40"
                >
                  Sil
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
