import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useMemo } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router'

import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Select } from '@/components/ui/Select'
import { Skeleton } from '@/components/ui/Skeleton'
import { useAuth } from '@/features/auth/useAuth'
import { useCategorySchema } from '@/features/catalog/hooks'
import {
  addFavorite,
  catalogueKeys,
  getListingFacets,
  removeFavorite,
  searchListings,
} from '@/features/listings/api'
import { FilterPanel } from '@/features/listings/FilterPanel'
import { formatNumber } from '@/features/listings/format'
import { ListingCard } from '@/features/listings/ListingCard'
import type { ListingCard as Card } from '@/features/listings/types'

/**
 * The catalogue and the search page are the same screen: a URL with filters in it. Every control
 * writes to the query string and nothing else, so a result set is shareable, survives the back
 * button and is ready for the crawler work in a later phase.
 */
export function CataloguePage() {
  const [params, setParams] = useSearchParams()
  const { categorySlug, subSlug } = useParams()
  const navigate = useNavigate()
  const { user } = useAuth()
  const queryClient = useQueryClient()

  // A category in the path wins over one in the query string, so /elanlar/kamp/cadirlar works.
  const pathCategory = subSlug ?? categorySlug

  const values = useMemo(() => {
    const entries: Record<string, string> = {}

    for (const [key, value] of params.entries()) {
      entries[key] = value
    }

    if (pathCategory) {
      entries['category'] = pathCategory
    }

    return entries
  }, [params, pathCategory])

  const schema = useCategorySchema(values['category'] ?? '')

  const results = useQuery({
    queryKey: catalogueKeys.search(values),
    queryFn: () => searchListings(values),
  })

  const facets = useQuery({
    queryKey: catalogueKeys.facets(values),
    queryFn: () => getListingFacets(values),
  })

  const update = useCallback(
    (next: Record<string, string | null>) => {
      const merged = new URLSearchParams(params)

      for (const [key, value] of Object.entries(next)) {
        if (value === null || value === '') {
          merged.delete(key)
        } else {
          merged.set(key, value)
        }
      }

      // Changing a filter starts again at the first page — but paging itself must not reset it.
      if (!('page' in next)) {
        merged.delete('page')
      }

      setParams(merged)
    },
    [params, setParams],
  )

  const favorite = useMutation({
    mutationFn: ({ listing }: { listing: Card }) =>
      listing.isFavorited ? removeFavorite(listing.shortId) : addFavorite(listing.shortId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['listings'] }),
  })

  function pickCategory(slug: string | null) {
    // A category chosen from the facet list becomes part of the path, not a parameter.
    const merged = new URLSearchParams(params)
    merged.delete('page')
    merged.delete('category')

    // Attribute filters belong to the category that was open; they mean nothing in the next one.
    for (const key of [...merged.keys()]) {
      if (key.startsWith('attr.')) {
        merged.delete(key)
      }
    }

    const search = merged.toString()
    void navigate(`${slug ? `/elanlar/${slug}` : '/elanlar'}${search ? `?${search}` : ''}`)
  }

  const total = results.data?.total ?? 0
  const totalLabel = results.data?.totalIsExact === false ? `${formatNumber(total)}+` : formatNumber(total)

  return (
    <div className="flex flex-col gap-5">
      <header className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold text-ink">
          {schema.data?.category.nameAz ?? (values['q'] ? `"${values['q']}" üzrə axtarış` : 'Elanlar')}
        </h1>

        {results.data ? (
          <p className="text-sm text-muted">{totalLabel} elan</p>
        ) : null}
      </header>

      <div className="grid gap-5 lg:grid-cols-[minmax(0,260px)_minmax(0,1fr)]">
        <FilterPanel
          schema={schema.data}
          values={values}
          onChange={update}
          onReset={() => pickCategory(null)}
          categoryFacets={facets.data?.categories ?? []}
          onPickCategory={pickCategory}
        />

        <section className="flex flex-col gap-4">
          <div className="flex flex-wrap items-center justify-end gap-2">
            <Select
              label="Sıralama"
              value={values['sort'] ?? 'newest'}
              onChange={(event) => update({ sort: event.target.value })}
            >
              <option value="newest">Əvvəlcə yenilər</option>
              <option value="price_asc">Əvvəlcə ucuz</option>
              <option value="price_desc">Əvvəlcə baha</option>
              {values['q'] ? <option value="relevance">Uyğunluğa görə</option> : null}
            </Select>
          </div>

          {results.isPending ? (
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4" aria-busy="true">
              {Array.from({ length: 8 }, (_, index) => (
                <Skeleton key={index} className="h-64" />
              ))}
            </div>
          ) : results.isError ? (
            <ErrorState
              description="Elanları yükləmək mümkün olmadı."
              onRetry={() => void results.refetch()}
            />
          ) : results.data.items.length === 0 ? (
            <EmptyState
              title="Uyğun elan tapılmadı."
              description="Filtrləri dəyişib yenidən yoxlayın."
            />
          ) : (
            <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4">
              {results.data.items.map((listing) => (
                <ListingCard
                  key={listing.shortId}
                  listing={listing}
                  onToggleFavorite={user ? (item) => favorite.mutate({ listing: item }) : undefined}
                />
              ))}
            </ul>
          )}

          {results.data && results.data.totalPages > 1 ? (
            <nav aria-label="Səhifələr" className="flex items-center justify-center gap-3 pt-2">
              <Button
                type="button"
                variant="secondary"
                size="sm"
                disabled={results.data.page <= 1}
                onClick={() => update({ page: String(results.data!.page - 1) })}
              >
                Əvvəlki
              </Button>

              <span className="text-sm text-muted">
                {results.data.page} / {results.data.totalPages}
              </span>

              <Button
                type="button"
                variant="secondary"
                size="sm"
                disabled={results.data.page >= results.data.totalPages}
                onClick={() => update({ page: String(results.data!.page + 1) })}
              >
                Növbəti
              </Button>
            </nav>
          ) : null}
        </section>
      </div>
    </div>
  )
}
