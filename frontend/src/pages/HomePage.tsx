import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { useQuery } from '@tanstack/react-query'

import { Badge } from '@/components/ui/Badge'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { catalogueKeys, searchListings } from '@/features/listings/api'
import { ListingCard } from '@/features/listings/ListingCard'
import { useCategoryTree, useRegions, useStaticPages } from '@/features/catalog/hooks'
import { CategoryIcon } from '@/features/home/CategoryIcon'

/**
 * The homepage, following the ten-section structure in the design reference
 * (docs/design-reference — "6. Homepage strukturu"). Sections 01 and 10, the header and footer,
 * belong to PublicLayout; everything between them lives here:
 *
 *   02 Hero            04 Seçilmiş elanlar   06 Kateqoriyaya görə   08 Outdoor bələdçi
 *   03 Kateqoriyalar   05 Son elanlar        07 Regionlar           09 CTA
 *
 * Every section is driven by the real taxonomy and listing APIs. Where the data does not exist
 * yet, the section keeps its designed shape and states plainly why it is empty, rather than being
 * dropped — an empty marketplace still has to read as a marketplace.
 */

/** How many latest listings the feed shows before handing off to the catalogue. */
const LATEST_PAGE_SIZE = 12

/** The four the design names for "Kateqoriyaya görə"; the rest stay in the tile grid above. */
const FEATURED_CATEGORY_SLUGS = ['ovculuq', 'baliqciliq', 'kamp', 'outdoor-geyim']

export function HomePage() {
  const navigate = useNavigate()
  const [term, setTerm] = useState('')
  const [regionSlug, setRegionSlug] = useState('')

  const categories = useCategoryTree()
  const regions = useRegions()
  const guides = useStaticPages('guide')

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
      {/* 02 — Hero. Full-bleed band with the content still on the 1280 grid, matching the design's
          own `<section>` + inner container. The negative margin escapes the layout's max width;
          `overflow-x: clip` on the body (styles/index.css) keeps that from ever becoming a
          sideways scroll.

          The design asks for an Azerbaijani outdoor scene here; until that photography exists the
          band carries the brand greens rather than a stock stand-in. */}
      <section className="mx-[calc(50%-50vw)] bg-brand bg-linear-to-b from-brand to-brand-deep text-white">
        <div className="mx-auto max-w-[1280px] px-4 py-12 sm:px-6 sm:py-16">
          <h1 className="max-w-2xl text-3xl leading-tight text-balance sm:text-[40px]">
            Təbiətə çıx. Lazım olanı tap.
          </h1>
          <p className="mt-3 max-w-xl text-white/75">
            Ov, balıqçılıq, kamp və outdoor avadanlıqları.
          </p>

          <form onSubmit={submitSearch} className="mt-7 flex flex-col gap-2 sm:flex-row">
            <div className="flex min-w-0 flex-1 overflow-hidden rounded-(--radius-input) bg-surface">
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
              <span className="text-xs text-white/60">Populyar:</span>
              {topCategories.slice(0, 5).map((category) => (
                <Link
                  key={category.slug}
                  to={`/elanlar/${category.slug}`}
                  className="rounded-full border border-white/25 px-3 py-1.5 text-xs font-medium text-white/90 transition hover:border-white hover:bg-white/10"
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
                  {/* The design's tile is the square plus a label beneath it — no card around
                      them — so the eight read as one row rather than eight boxes. */}
                  <Link
                    to={`/elanlar/${category.slug}`}
                    className="group flex h-full flex-col items-center gap-2 text-center"
                  >
                    <span className="flex aspect-square w-full items-center justify-center rounded-(--radius-card) bg-interactive-soft text-interactive transition-colors group-hover:bg-interactive group-hover:text-white">
                      <CategoryIcon iconKey={category.iconKey} className="h-12 w-12" />
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

        {/* 06 — Kateqoriyaya görə. The four the design names, each opened up one level. */}
        {topCategories.length > 0 ? (
          <section className="pb-10">
            <h2 className="mb-5 text-xl">Kateqoriyaya görə</h2>

            <div className="grid gap-3.5 sm:grid-cols-2 lg:grid-cols-4">
              {topCategories
                .filter((category) => FEATURED_CATEGORY_SLUGS.includes(category.slug))
                .map((category) => (
                  <div
                    key={category.slug}
                    className="rounded-(--radius-card) border border-line bg-surface p-5"
                  >
                    <div className="flex items-center gap-2.5">
                      <span className="text-interactive">
                        <CategoryIcon iconKey={category.iconKey} className="h-5 w-5" />
                      </span>
                      <h3 className="text-base">
                        <Link to={`/elanlar/${category.slug}`} className="hover:text-interactive">
                          {category.nameAz}
                        </Link>
                      </h3>
                    </div>

                    <ul className="mt-3 space-y-1.5">
                      {category.children.slice(0, 6).map((child) => (
                        <li key={child.slug}>
                          <Link
                            to={`/elanlar/${category.slug}/${child.slug}`}
                            className="text-sm text-muted hover:text-interactive"
                          >
                            {child.nameAz}
                          </Link>
                        </li>
                      ))}
                    </ul>
                  </div>
                ))}
            </div>
          </section>
        ) : null}

        {/* 07 — Regionlar. The design shows a map; until one exists the same discovery is offered
            as the region list the catalogue already filters by. */}
        {(regions.data ?? []).length > 0 ? (
          <section className="pb-10">
            <div className="mb-5 flex items-baseline justify-between gap-4">
              <h2 className="text-xl">Regionlar üzrə</h2>
              <Link to="/elanlar" className="text-sm font-semibold text-interactive hover:text-accent">
                Bütün regionlar →
              </Link>
            </div>

            <ul className="flex flex-wrap gap-2">
              {(regions.data ?? []).slice(0, 18).map((region) => (
                <li key={region.slug}>
                  <Link
                    to={`/elanlar?region=${region.slug}`}
                    className="inline-flex items-center gap-2 rounded-full border border-line bg-surface px-3.5 py-2 text-sm text-ink transition-colors hover:border-interactive"
                  >
                    {region.nameAz}
                    <span className="text-xs text-faint tabular-nums">{region.listingCount}</span>
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        ) : null}

        {/* 08 — Outdoor bələdçi. */}
        <section className="pb-10">
          <div className="mb-5 flex items-baseline justify-between gap-4">
            <h2 className="text-xl">Outdoor bələdçi</h2>
            <Link to="/beledci" className="text-sm font-semibold text-interactive hover:text-accent">
              Bütün məqalələr →
            </Link>
          </div>

          {(guides.data ?? []).length > 0 ? (
            <ul className="grid gap-3.5 sm:grid-cols-2 lg:grid-cols-3">
              {(guides.data ?? []).slice(0, 3).map((page) => (
                <li key={page.slug}>
                  <Link
                    to={`/beledci/${page.slug}`}
                    className="flex h-full flex-col rounded-(--radius-card) border border-line bg-surface p-5 transition-colors hover:border-interactive"
                  >
                    <h3 className="text-base">{page.titleAz}</h3>
                    {page.excerptAz ? (
                      <p className="mt-2 line-clamp-3 text-sm text-muted">{page.excerptAz}</p>
                    ) : null}
                  </Link>
                </li>
              ))}
            </ul>
          ) : (
            <div className="rounded-(--radius-card) border border-dashed border-line bg-surface px-6 py-10 text-center">
              <p className="text-ink">Bələdçi məqalələri hazırlanır.</p>
              <p className="mx-auto mt-2 max-w-md text-sm text-muted">
                Avadanlıq seçimi və outdoor təcrübəsi üzrə materiallar tezliklə burada olacaq.
              </p>
            </div>
          )}
        </section>

        {/* 09 — CTA. */}
        <section className="pb-14">
          <div className="flex flex-col items-start gap-5 rounded-(--radius-card) bg-brand px-6 py-9 text-white sm:flex-row sm:items-center sm:justify-between sm:px-9">
            <div>
              <h2 className="text-2xl text-white">Sən də elanını yerləşdir</h2>
              <p className="mt-2 max-w-lg text-white/75">
                Ov, balıqçılıq, kamp və outdoor avadanlığını Azərbaycan üzrə alıcılara çatdır.
              </p>
            </div>
            <Link
              to="/yeni-elan"
              className="inline-flex h-12 shrink-0 items-center rounded-(--radius-button) bg-accent px-6 font-semibold text-white transition hover:brightness-95"
            >
              Yeni elan
            </Link>
          </div>
        </section>
      </div>
    </div>
  )
}
