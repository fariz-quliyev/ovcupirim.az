/**
 * One glyph per top-level category, keyed on the taxonomy's own `iconKey`.
 *
 * The design calls for a photo tile per category. No category photography exists yet and the
 * taxonomy's `imageKey` is unset throughout, so these glyphs stand in at the same square footprint
 * the tiles reserve — swapping them for imagery later is a change inside the tile, not a change to
 * the grid around it.
 *
 * Deliberately no firearm: the design reference is explicit that the brand must not read as a
 * weapons marketplace ("Brend yalnız silahla assosiasiya edilməməlidir"), so hunting is drawn as
 * a tracking target rather than a rifle.
 */
const paths: Record<string, string> = {
  // Ovçuluq — a sighting reticle.
  ov: 'M12 3v3m0 12v3M3 12h3m12 0h3M12 8.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7Z',
  // Balıqçılıq — a fish.
  baliq: 'M3 12c3.2-4 6.4-6 9.6-6 3.2 0 6 2 8.4 6-2.4 4-5.2 6-8.4 6-3.2 0-6.4-2-9.6-6Zm0 0 3 3m-3-3 3-3m11.8 2.2h.01',
  // Kamp — a tent.
  kamp: 'M12 4 3.5 19h17L12 4Zm0 0v15m0 0 4-7m-4 7-4-7',
  // Outdoor geyim — a jacket.
  geyim: 'M9 3 5 5.5V21h14V5.5L15 3l-3 2.5L9 3Zm3 2.5V21',
  // Çanta və aksesuar — a backpack.
  canta: 'M7 8V6.5a5 5 0 0 1 10 0V8m-11 0h12a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2v-9a2 2 0 0 1 2-2Zm3 4h6',
  // Outdoor nəqliyyat — an off-road vehicle.
  neqliyyat: 'M3 14h18m-15 0V9.5L8 6h8l2 3.5V14M7.5 18.5a1.75 1.75 0 1 0 0-3.5 1.75 1.75 0 0 0 0 3.5Zm9 0a1.75 1.75 0 1 0 0-3.5 1.75 1.75 0 0 0 0 3.5Z',
  // Optika və durbin — binoculars.
  optika: 'M7 5h3v9H7zm7 0h3v9h-3zM10 8h4M7 14a2.5 2.5 0 0 0 5 0m0 0a2.5 2.5 0 0 0 5 0',
  // Bıçaq və alət — a knife.
  bicaq: 'M4 16 14 6a4 4 0 0 1 5 5l-3 3m-12 2 3 3m-3-3 5-1m4-4 3 3',
}

/** Drawn when the taxonomy carries a key this component has no glyph for. */
const fallback = 'M4 19V7.5L12 4l8 3.5V19m-16 0h16m-11 0v-5h6v5'

interface CategoryIconProps {
  iconKey: string | null
  className?: string
}

export function CategoryIcon({ iconKey, className = '' }: CategoryIconProps) {
  const d = (iconKey && paths[iconKey]) || fallback

  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className={className}
    >
      <path d={d} />
    </svg>
  )
}
