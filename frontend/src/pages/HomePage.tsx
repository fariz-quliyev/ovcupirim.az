import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { useQuery } from '@tanstack/react-query'

import { Badge } from '@/components/ui/Badge'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { catalogueKeys, searchListings } from '@/features/listings/api'
import { ListingCard } from '@/features/listings/ListingCard'
import { useCategoryTree, useRegions } from '@/features/catalog/hooks'
import { CategoryIcon } from '@/features/home/CategoryIcon'

/**
 * The homepage: search, categories, promoted listings, newest listings — in that order, and
 * nothing else. The header and footer belong to PublicLayout.
 *
 * It is deliberately short. A classifieds visitor arrives to search or to browse, so the page puts
 * both within the first screen and then gets out of the way; the editorial sections that once sat
 * below the feed (by-category, regions, guide, a closing call to action) pushed the listings down
 * without helping anyone find anything, and the catalogue, category and region pages already cover
 * that ground.
 *
 * Every section is driven by the real taxonomy and listing APIs. Where the data does not exist
 * yet, the section keeps its shape and states plainly why it is empty rather than being dropped —
 * an empty marketplace still has to read as a marketplace.
 */

/** How many latest listings the feed shows before handing off to the catalogue. */
const LATEST_PAGE_SIZE = 12

/**
 * Where a category picture lives once an administrator sets one.
 *
 * The API hands back the storage key rather than a URL — unlike a listing, which is resolved
 * server-side — so the public path is composed here. It matches Storage:Local:PublicBaseUrl, which
 * is a relative `/uploads` in every environment. An absolute URL is passed through untouched, so a
 * key that already points somewhere else keeps working.
 */
function categoryImageUrl(imageKey: string): string {
  return /^https?:\/\//i.test(imageKey) ? imageKey : `/uploads/${imageKey.replace(/^\/+/, '')}`
}

export function HomePage() {
  const navigate = useNavigate()
  const [term, setTerm] = useState('')
  const [regionSlug, setRegionSlug] = useState('')

  const categories = useCategoryTree()
  const regions = useRegions()

  const latest = useQuery({
    queryKey: catalogueKeys.search({ sort: 'newest', pageSize: String(LATEST_PAGE_SIZE) }),
    queryFn: () => searchListings({ sort: 'newest', pageSize: String(LATEST_PAGE_SIZE) }),
  })

  function submitSearch(event: React.FormEvent) {
    event.preventDefault()

    const params = new URLSearchParams()
    if (term.trim()) params.set('q', term.trim())
    if (regionSlug) params.set('region', regionSlug)

    const query = params.toString()
    void navigate(query ? `/axtaris?${query}` : '/elanlar')
  }

  const topCategories = categories.data ?? []
  const listings = latest.data?.items ?? []

  return (
    <div>
      {/* Search band. Full-bleed, with the content still on the 1280 grid, matching the design's
          own `<section>` + inner container. The negative margin escapes the layout's max width;
          `overflow-x: clip` on the body (styles/index.css) keeps that from ever becoming a
          sideways scroll.

          No headline above it, deliberately: a classifieds visitor arrives wanting to search or to
          browse categories, so both are on screen immediately. That is how the design reference's
          own homepage opens, and how the marketplace this one is modelled on does it. White with a
          hairline rule beneath (`background:#FFFFFF; border-bottom:1px solid #E4E7E2`); the green
          belongs to the sticky header above. */}
      <section className="mx-[calc(50%-50vw)] border-b border-line bg-surface">
        <div className="mx-auto max-w-[1280px] px-4 py-6 sm:px-6">
          <form onSubmit={submitSearch} className="flex flex-col gap-2 sm:flex-row">
            <div className="flex min-w-0 flex-1 overflow-hidden rounded-(--radius-input) border border-line bg-surface">
              <input
                value={term}
                onChange={(event) => setTerm(event.target.value)}
                aria-label="Avadanlıq və ya marka axtarışı"
                placeholder="Avadanlıq və ya marka axtarışı"
                className="min-w-0 flex-1 px-4 text-[15px] text-ink outline-none"
              />
              <select
                value={regionSlug}
                onChange={(event) => setRegionSlug(event.target.value)}
                aria-label="Region"
                className="hidden h-12 border-l border-line bg-surface px-3 text-sm text-ink outline-none sm:block"
              >
                <option value="">Bütün regionlar</option>
                {(regions.data ?? []).map((region) => (
                  <option key={region.slug} value={region.slug}>
                    {region.nameAz}
                  </option>
                ))}
              </select>
            </div>

            <button
              type="submit"
              className="h-12 shrink-0 rounded-(--radius-input) bg-accent px-8 font-semibold text-white transition hover:brightness-95 sm:rounded-s-none"
            >
              Axtar
            </button>
          </form>

          {topCategories.length > 0 ? (
            <div className="mt-4 flex flex-wrap items-center gap-2">
              <span className="text-xs text-muted">Populyar:</span>
              {topCategories.slice(0, 5).map((category) => (
                <Link
                  key={category.slug}
                  to={`/elanlar/${category.slug}`}
                  className="rounded-full border border-line px-3 py-1.5 text-xs font-medium text-ink transition-colors hover:border-interactive hover:bg-canvas"
                >
                  {category.nameAz}
                </Link>
              ))}
            </div>
          ) : null}
        </div>
      </section>

      <div>
        {/* 03 — Kateqoriyalar. Eight tiles on desktop, a single swipe row on a phone. */}
        <section className="py-10">
          <div className="mb-5 flex items-baseline justify-between gap-4">
            <h2 className="text-xl">Kateqoriyalar</h2>
            <Link to="/kateqoriyalar" className="text-sm font-semibold text-interactive hover:text-accent">
              Hamısı →
            </Link>
          </div>

          {categories.isPending ? (
            <div className="grid grid-cols-3 gap-3 sm:grid-cols-4 lg:grid-cols-8" aria-busy="true">
              {Array.from({ length: 8 }, (_, i) => (
                <Skeleton key={i} className="aspect-square" />
              ))}
            </div>
          ) : null}

          {categories.isError ? <ErrorState onRetry={() => void categories.refetch()} /> : null}

          {topCategories.length > 0 ? (
            <ul className="grid grid-cols-3 gap-3 sm:grid-cols-4 lg:grid-cols-8">
              {topCategories.map((category) => (
                <li key={category.slug}>
                  {/* The square plus a label beneath it, no card around them, so the eight read as
                      one row rather than eight boxes. The square shows the category's own picture
                      when an administrator has set one and falls back to a glyph until then, so
                      adding photography later changes what is inside the tile, not the grid. */}
                  <Link
                    to={`/elanlar/${category.slug}`}
                    className="group flex h-full flex-col items-center gap-2 text-center"
                  >
                    <span className="flex aspect-square w-full items-center justify-center overflow-hidden rounded-(--radius-card) bg-interactive-soft text-interactive transition-colors group-hover:bg-interactive group-hover:text-white">
                      {category.imageKey ? (
                        <img
                          src={categoryImageUrl(category.imageKey)}
                          alt=""
                          loading="lazy"
                          className="h-full w-full object-cover"
                        />
                      ) : (
                        <CategoryIcon iconKey={category.iconKey} className="h-12 w-12" />
                      )}
                    </span>
                    <span className="text-[13px] font-medium text-interactive text-pretty">
                      {category.nameAz}
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          ) : null}
        </section>

        {/* 04 — Seçilmiş elanlar. The paid placement the design reserves for VIP. No listing can
            be promoted until the payment gateway is live, so the section states that rather than
            quietly disappearing. */}
        <section className="pb-10">
          <div className="mb-5 flex items-baseline justify-between gap-4">
            <div className="flex items-center gap-2.5">
              <h2 className="text-xl">Premium elanlar</h2>
              <Badge tone="top">VIP</Badge>
            </div>
            <Link to="/elanlar" className="text-sm font-semibold text-interactive hover:text-accent">
              Hamısına bax →
            </Link>
          </div>

          <div className="rounded-(--radius-card) border border-dashed border-line bg-surface px-6 py-10 text-center">
            <p className="text-ink">Hazırda premium elan yoxdur.</p>
            <p className="mx-auto mt-2 max-w-md text-sm text-muted">
              Satıcılar elanlarını irəli çəkdikcə bu bölmə doldurulacaq.
            </p>
          </div>
        </section>

        {/* 05 — Son elanlar. The main marketplace content. */}
        <section className="pb-10">
          <div className="mb-5 flex items-baseline justify-between gap-4">
            <h2 className="text-xl">Son elanlar</h2>
            <Link to="/elanlar" className="text-sm font-semibold text-interactive hover:text-accent">
              Bütün elanlar →
            </Link>
          </div>

          {latest.isPending ? (
            <div
              className="grid grid-cols-2 gap-3.5 md:grid-cols-3 lg:grid-cols-4"
              aria-busy="true"
            >
              {Array.from({ length: 8 }, (_, i) => (
                <Skeleton key={i} className="h-[300px]" />
              ))}
            </div>
          ) : null}

          {latest.isError ? <ErrorState onRetry={() => void latest.refetch()} /> : null}

          {latest.isSuccess && listings.length > 0 ? (
            <ul className="grid grid-cols-2 gap-3.5 md:grid-cols-3 lg:grid-cols-4">
              {listings.map((listing) => (
                <ListingCard key={listing.shortId} listing={listing} />
              ))}
            </ul>
          ) : null}

          {latest.isSuccess && listings.length === 0 ? (
            <div className="rounded-(--radius-card) border border-dashed border-line bg-surface px-6 py-12 text-center">
              <p className="text-ink">Hələ dərc olunmuş elan yoxdur.</p>
              <p className="mx-auto mt-2 max-w-md text-sm text-muted">
                İlk elanı sən yerləşdir — hər elan moderasiyadan keçdikdən sonra burada görünür.
              </p>
              <Link
                to="/yeni-elan"
                className="mt-5 inline-flex h-11 items-center rounded-(--radius-button) bg-accent px-5 font-semibold text-white transition hover:brightness-95"
              >
                Elan yerləşdir
              </Link>
            </div>
          ) : null}
        </section>

      </div>
    </div>
  )
}
