import type { ReactNode } from 'react'

/**
 * The hunter who could not find it.
 *
 * The message is lettered into the artwork, so nothing here repeats it: a screen reader gets it
 * from the alt text, and a phone — where that lettering is too small to read — gets it back as real
 * text below. What the picture cannot be is the way out, so the caller supplies that.
 *
 * Shared between a bad URL and a listing that no longer exists, which are the same thing to whoever
 * followed the link.
 */
export function NotFoundArtwork({ action }: { action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-6 py-8 sm:py-14">
      <img
        src="/404-elan-tapilmadi.webp"
        alt="404 — axtardığınız elan tapılmadı"
        width={1672}
        height={530}
        className="w-full max-w-4xl rounded-(--radius-card)"
      />

      <p className="text-center text-lg font-semibold text-ink sm:hidden">
        Axtardığını elan mövcud deyil
      </p>

      {action}
    </div>
  )
}
