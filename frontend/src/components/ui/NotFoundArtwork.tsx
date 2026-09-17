import type { ReactNode } from 'react'

/**
 * Imported rather than referenced from `public/`, so the build gives it a name derived from its
 * contents. A fixed name is served with a four-hour cache, which means replacing the picture leaves
 * everyone who has already seen it looking at the old one for the rest of the afternoon — the same
 * trap the category pictures fell into. A new picture is a new name, and there is nothing to
 * purge.
 */
import artwork from '@/assets/404-elan-tapilmadi.webp'
import artworkWide from '@/assets/404-elan-tapilmadi-genis.webp'

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
      {/* Two versions of one scene, because the shape that works on a monitor is the wrong shape on
          a phone.

          The wide one, 2.54 times wider than it is tall, is what lets the picture run the full
          width of a desktop screen: at 1920 it stands 756 tall and the sentence and the way back
          still land above the fold. The original proportions would have stood 1184 tall there and
          pushed both off the screen, which is the one thing a 404 must not do. Below 640 the trade
          reverses — the wide scene would be a 154px strip with a hunter too small to read — so a
          phone gets the original, which at that width is a comfortable 241 tall.

          The band itself is the real viewport width, not `100vw`: see --scrollbar-width. */}
      <picture className="mx-[calc((100%_-_100vw_+_var(--scrollbar-width))_/_2)] w-[calc(100vw_-_var(--scrollbar-width))] max-w-none">
        <source media="(min-width: 640px)" srcSet={artworkWide} width={1997} height={787} />
        <img src={artwork} alt="" width={1597} height={985} className="w-full" />
      </picture>

      <p className="text-center text-xl font-semibold text-ink">{message}</p>

      {action}
    </div>
  )
}
