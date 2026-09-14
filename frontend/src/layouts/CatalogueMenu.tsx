import { useEffect, useState } from 'react'
import { Link } from 'react-router'

import { categoryImageUrl } from '@/features/catalog/categoryImage'
import { useCategoryTree } from '@/features/catalog/hooks'
import { CategoryIcon } from '@/features/home/CategoryIcon'
import type { CategoryNode } from '@/features/catalog/types'

/**
 * The catalogue panel behind the header's "Kataloq" button: top-level categories down the left,
 * the selected one's subcategories down the right.
 *
 * Measured against the marketplace this is modelled on rather than guessed at — there, the panel is
 * a full-viewport-width white sheet directly below the header, with no shadow and no dimming behind
 * it, and it opens on hover as well as on click.
 *
 * The left column is a list of links, not buttons. Each one is a real destination, and pointing at
 * it — or tabbing to it — swaps the right column, so a keyboard reader walks the same path a mouse
 * does instead of a parallel one built out of buttons that go nowhere.
 */

/** Guards against the panel being taller than a laptop screen once the taxonomy grows. */
const PANEL_MAX_HEIGHT = 'max-h-[min(70vh,34rem)]'

function Chevron({ className = '' }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      className={`size-4 shrink-0 ${className}`}
      aria-hidden="true"
    >
      <path strokeLinecap="round" strokeLinejoin="round" d="m9 5 7 7-7 7" />
    </svg>
  )
}

function CategoryThumb({ category }: { category: CategoryNode }) {
  if (category.imageKey) {
    return (
      <img
        src={categoryImageUrl(category.imageKey)}
        alt=""
        loading="lazy"
        className="size-9 shrink-0 rounded-(--radius-button) object-cover"
      />
    )
  }

  return (
    <span className="grid size-9 shrink-0 place-items-center rounded-(--radius-button) bg-canvas text-interactive">
      <CategoryIcon iconKey={category.iconKey} className="size-5" />
    </span>
  )
}

export function CatalogueMenu({ onClose }: { onClose: () => void }) {
  const categories = useCategoryTree()
  const tree = categories.data ?? []

  const [activeSlug, setActiveSlug] = useState<string | null>(null)

  // Nothing is selected when the panel opens: the second column fills in only once a category is
  // pointed at or tabbed to. Pre-selecting the first one would show a set of subcategories nobody
  // asked for, and highlight a row the reader's eye had not landed on yet.
  const active = tree.find((category) => category.slug === activeSlug) ?? null

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        onClose()
      }
    }

    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  return (
    <>
      {/* Catches a click anywhere else. Transparent rather than dimmed, matching the reference:
          this is a menu hanging off the header, not a modal that takes over the page. */}
      <button
        type="button"
        tabIndex={-1}
        aria-hidden="true"
        onClick={onClose}
        className="fixed inset-0 z-30 cursor-default"
      />

      <div
        id="catalogue-menu"
        className="absolute inset-x-0 top-full z-40 border-b border-line bg-surface"
      >
        <div className="mx-auto max-w-[1280px] px-4 sm:px-6">
          {categories.isPending ? (
            <p className="py-10 text-sm text-muted">Kateqoriyalar yüklənir…</p>
          ) : tree.length === 0 ? (
            <p className="py-10 text-sm text-muted">Kateqoriyalar hazırda əlçatan deyil.</p>
          ) : (
            <div className={`grid gap-x-8 py-4 md:grid-cols-[minmax(0,22rem)_1fr] ${PANEL_MAX_HEIGHT}`}>
              <nav aria-label="Kateqoriyalar" className="overflow-y-auto border-line md:border-e md:pe-4">
                <ul>
                  {tree.map((category) => {
                    const isActive = category.slug === active?.slug

                    return (
                      <li key={category.slug}>
                        <Link
                          to={`/elanlar/${category.slug}`}
                          onMouseEnter={() => setActiveSlug(category.slug)}
                          onFocus={() => setActiveSlug(category.slug)}
                          onClick={onClose}
                          className={`flex items-center gap-3 rounded-(--radius-button) px-3 py-2.5 text-[15px] transition-colors ${
                            isActive
                              ? 'bg-interactive-soft font-semibold text-interactive'
                              : 'text-ink hover:bg-canvas'
                          }`}
                        >
                          <CategoryThumb category={category} />
                          <span className="min-w-0 flex-1 truncate">{category.nameAz}</span>
                          <Chevron className={isActive ? '' : 'text-faint'} />
                        </Link>
                      </li>
                    )
                  })}
                </ul>
              </nav>

              {active ? (
                <nav aria-label={`${active.nameAz} alt kateqoriyaları`} className="overflow-y-auto">
                  {active.children.length > 0 ? (
                    // One column, like the reference. Two columns flow down-then-across, which
                    // reads as an arbitrary split on the short lists most categories have.
                    <ul className="max-w-lg">
                      {active.children.map((child) => (
                        <li key={child.slug}>
                          <Link
                            to={`/elanlar/${child.slug}`}
                            onClick={onClose}
                            className="flex items-center gap-3 rounded-(--radius-button) px-3 py-2.5 text-[15px] text-ink transition-colors hover:bg-canvas hover:text-interactive"
                          >
                            <span className="min-w-0 flex-1 truncate">{child.nameAz}</span>
                            <Chevron className="text-faint" />
                          </Link>
                        </li>
                      ))}
                    </ul>
                  ) : (
                    <p className="px-3 py-2.5 text-sm text-muted">
                      Bu bölmədə hələ alt kateqoriya yoxdur.
                    </p>
                  )}

                  <Link
                    to={`/elanlar/${active.slug}`}
                    onClick={onClose}
                    className="mt-2 inline-flex items-center gap-1 px-3 py-2 text-sm font-semibold text-interactive hover:text-brand"
                  >
                    {active.nameAz} — hamısı
                    <Chevron />
                  </Link>
                </nav>
              ) : (
                // The column keeps its width so the panel does not resize the moment a category is
                // pointed at, and says what to do rather than sitting blank.
                <p className="hidden px-3 py-2.5 text-sm text-muted md:block">
                  Alt bölmələri görmək üçün kateqoriyanın üzərinə gəlin.
                </p>
              )}
            </div>
          )}
        </div>
      </div>
    </>
  )
}
