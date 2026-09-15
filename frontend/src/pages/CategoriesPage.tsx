import { NavLink } from 'react-router'

import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { CategoryThumb } from '@/features/catalog/CategoryThumb'
import { PhoneTakeoverBar } from '@/features/catalog/PhoneTakeoverBar'
import { useCategoryTree } from '@/features/catalog/hooks'
import type { CategoryNode } from '@/features/catalog/types'

/**
 * The full catalogue.
 *
 * Two presentations of one list. On a phone it is a row per category — picture, name, and the
 * subcategories it holds on one grey line — which is what a catalogue screen on this market looks
 * like and what fits a narrow column. From `sm` up the cards come back, because a wide screen has
 * room to show every subcategory as its own link rather than a summary of them.
 */

function AgeBadge() {
  return (
    <span
      className="shrink-0 rounded bg-canvas px-2 py-0.5 text-xs text-muted"
      title="Bu kateqoriya üçün təsnifat gözlənilir"
    >
      18+
    </span>
  )
}

function Chevron() {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      className="size-4 shrink-0 text-faint"
      aria-hidden="true"
    >
      <path strokeLinecap="round" strokeLinejoin="round" d="m9 5 7 7-7 7" />
    </svg>
  )
}

/** The subcategory names, as the one-line summary the row shows beneath the category. */
function summarise(category: CategoryNode): string | null {
  return category.children.length > 0
    ? category.children.map((child) => child.nameAz).join(', ')
    : null
}

export function CategoriesPage() {
  const { data, isPending, isError, refetch } = useCategoryTree()

  return (
    <div className="sm:py-10">
      <PhoneTakeoverBar title="Kataloq" />

      <h1 className="hidden text-2xl sm:block">Kateqoriyalar</h1>
      <p className="mt-2 hidden text-muted sm:block">
        Ov, balıqçılıq, kamp və outdoor avadanlıqları üzrə bütün bölmələr.
      </p>

      {isPending ? (
        <div className="mt-6 grid gap-4 sm:mt-8 sm:grid-cols-2 lg:grid-cols-3" aria-busy="true">
          {Array.from({ length: 6 }, (_, i) => (
            <Skeleton key={i} className="h-20 sm:h-48" />
          ))}
        </div>
      ) : null}

      {isError ? (
        <div className="mt-8">
          <ErrorState title="Kateqoriyaları yükləmək mümkün olmadı" onRetry={() => void refetch()} />
        </div>
      ) : null}

      {data && data.length === 0 ? (
        <div className="mt-8">
          <EmptyState title="Kateqoriya yoxdur" description="Kateqoriya siyahısı hələ hazırlanmayıb." />
        </div>
      ) : null}

      {data && data.length > 0 ? (
        <>
          {/* Phone: one tappable row per category, bled to both screen edges so the dividers run
              the full width the way a native list does. */}
          <ul className="-mx-4 divide-y divide-line border-b border-line bg-surface sm:hidden">
            {data.map((category) => {
              const summary = summarise(category)

              return (
                <li key={category.slug}>
                  {/* Into the category's own screen, not straight to its listings: a phone has no
                      room to show the subcategories inline, so they get a screen of their own. */}
                  <NavLink
                    to={`/kateqoriyalar/${category.slug}`}
                    className="flex items-center gap-3 px-4 py-3 active:bg-canvas"
                  >
                    <CategoryThumb category={category} className="size-12" iconClassName="size-6" />

                    <span className="min-w-0 flex-1">
                      <span className="flex items-center gap-2">
                        <span className="truncate font-medium text-ink">{category.nameAz}</span>
                        {category.requiresAgeConfirmation ? <AgeBadge /> : null}
                      </span>

                      {summary ? (
                        <span className="mt-0.5 block truncate text-sm text-muted">{summary}</span>
                      ) : null}
                    </span>

                    <Chevron />
                  </NavLink>
                </li>
              )
            })}
          </ul>

          {/* Wider screens: every subcategory as its own link. */}
          <div className="mt-8 hidden gap-4 sm:grid sm:grid-cols-2 lg:grid-cols-3">
            {data.map((category) => (
              <section
                key={category.slug}
                className="rounded-(--radius-card) border border-line bg-surface p-5"
              >
                <div className="flex items-baseline justify-between gap-2">
                  <NavLink
                    to={`/elanlar/${category.slug}`}
                    className="font-heading text-lg font-bold text-ink hover:text-interactive"
                  >
                    {category.nameAz}
                  </NavLink>

                  {category.requiresAgeConfirmation ? <AgeBadge /> : null}
                </div>

                <ul className="mt-3 space-y-1.5">
                  {category.children.map((child) => (
                    <li key={child.slug}>
                      <NavLink
                        to={`/elanlar/${category.slug}/${child.slug}`}
                        className="text-sm text-muted hover:text-interactive"
                      >
                        {child.nameAz}
                      </NavLink>
                    </li>
                  ))}
                </ul>
              </section>
            ))}
          </div>
        </>
      ) : null}
    </div>
  )
}
