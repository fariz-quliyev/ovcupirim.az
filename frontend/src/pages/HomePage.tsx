import { Link } from 'react-router'
import { useQuery } from '@tanstack/react-query'

import { Badge } from '@/components/ui/Badge'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { catalogueKeys, searchListings } from '@/features/listings/api'
import { ListingCard } from '@/features/listings/ListingCard'
import { categoryImageUrl } from '@/features/catalog/categoryImage'
import { useCategoryTree } from '@/features/catalog/hooks'
import { CategoryIcon } from '@/features/home/CategoryIcon'

/**
 * The homepage: categories, promoted listings, newest listings — in that order, and nothing else.
 * The header and footer belong to PublicLayout, and search now lives in the header, on every page,
 * rather than in a band this page owned alone.
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

export function HomePage() {
  const categories = useCategoryTree()

  const latest = useQuery({
    queryKey: catalogueKeys.search({ sort: 'newest', pageSize: String(LATEST_PAGE_SIZE) }),
    queryFn: () => searchListings({ sort: 'newest', pageSize: String(LATEST_PAGE_SIZE) }),
  })

  const topCategories = categories.data ?? []
  const listings = latest.data?.items ?? []

  return (
    <div>
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
                className="mt-5 inline-flex h-11 items-center rounded-(--radius-button) bg-cta px-5 font-semibold text-white transition hover:brightness-95"
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
