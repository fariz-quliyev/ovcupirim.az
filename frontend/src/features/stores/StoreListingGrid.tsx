import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router'

import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Select } from '@/components/ui/Select'
import { Skeleton } from '@/components/ui/Skeleton'
import { useAuth } from '@/features/auth/useAuth'
import { addFavorite, removeFavorite } from '@/features/listings/api'
import { formatNumber } from '@/features/listings/format'
import { ListingCard } from '@/features/listings/ListingCard'
import type { ListingCard as Card } from '@/features/listings/types'

import { getStoreListings, storeKeys } from './api'

/**
 * The storefront's own listings. Deliberately the same contract as /elanlar — the server pins the
 * query to one store and applies the identical visibility rules — so this is a filtered catalogue
 * rather than a second listing implementation.
 */
export function StoreListingGrid({ slug }: { slug: string }) {
  const [params, setParams] = useSearchParams()
  const { user } = useAuth()
  const queryClient = useQueryClient()

  const values = useMemo(() => {
    const entries: Record<string, string> = {}

    for (const [key, value] of params.entries()) {
      entries[key] = value
    }

    return entries
  }, [params])

  const results = useQuery({
    queryKey: storeKeys.listings(slug, values),
    queryFn: () => getStoreListings(slug, values),
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

      // Changing the sort starts again at the first page; paging itself must not reset it.
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

  const total = results.data?.total ?? 0

  return (
    <section className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-base font-semibold text-ink">
          Mağazanın elanları{results.data ? ` · ${formatNumber(total)}` : ''}
        </h2>

        <Select
          label="Sıralama"
          value={values['sort'] ?? 'newest'}
          onChange={(event) => update({ sort: event.target.value })}
        >
          <option value="newest">Əvvəlcə yenilər</option>
          <option value="price_asc">Əvvəlcə ucuz</option>
          <option value="price_desc">Əvvəlcə baha</option>
        </Select>
      </div>

      {results.isPending ? (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4" aria-busy="true">
          {Array.from({ length: 4 }, (_, index) => (
            <Skeleton key={index} className="h-64" />
          ))}
        </div>
      ) : results.isError ? (
        <ErrorState
          description="Mağazanın elanlarını yükləmək mümkün olmadı."
          onRetry={() => void results.refetch()}
        />
      ) : results.data.items.length === 0 ? (
        <EmptyState title="Bu mağazada hazırda aktiv elan yoxdur." />
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
  )
}
