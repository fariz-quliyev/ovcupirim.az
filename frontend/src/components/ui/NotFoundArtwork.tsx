import type { ReactNode } from 'react'

/**
 * Imported rather than referenced from `public/`, so the build gives it a name derived from its
 * contents. A fixed name is served with a four-hour cache, which means replacing the picture leaves
 * everyone who has already seen it looking at the old one for the rest of the afternoon — the same
 * trap the category pictures fell into. A new picture is a new name, and there is nothing to
 * purge.
 */
import artwork from '@/assets/404-elan-tapilmadi.webp'

/**
 * The hunter who could not find it.
 *
 * The line beneath the picture is real text rather than part of the artwork. That is what lets one
 * illustration serve every kind of 404 — a listing, a page, whatever comes next — since only the
 * sentence changes. It also renders at a readable size on a phone, where lettering baked into a
 * 1597px-wide scene comes out at about seven pixels, and it can be corrected without repainting.
 *
 * The wooden sign inside the scene still reads "elan tapılmadı"; that is the artwork's own voice,
 * and the line below is what states the particular case.
 */
export function NotFoundArtwork({
  message,
  action,
}: {
  message: string
  action?: ReactNode
}) {
  return (
    <div className="flex flex-col items-center gap-5 py-8 sm:py-14">
      <img
        src={artwork}
        alt=""
        width={1597}
        height={985}
        className="w-full max-w-4xl rounded-(--radius-card)"
      />

      <p className="text-center text-xl font-semibold text-ink">{message}</p>

      {action}
    </div>
  )
}
