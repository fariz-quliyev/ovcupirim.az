import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { catalogueKeys, getFavorites, removeFavorite } from '@/features/listings/api'
import { ListingCard } from '@/features/listings/ListingCard'

/** "Seçilmişlər" — what this visitor saved, most recent first. */
export function FavoritesPage() {
  const queryClient = useQueryClient()

  const favorites = useQuery({
    queryKey: catalogueKeys.favorites(1),
    queryFn: () => getFavorites(1),
  })

  const remove = useMutation({
    mutationFn: removeFavorite,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['listings'] }),
  })

  return (
    <div className="flex flex-col gap-5">
      <h1 className="text-2xl font-semibold text-ink">Seçilmişlər</h1>

      {favorites.isPending ? (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4" aria-busy="true">
          <Skeleton className="h-64" />
          <Skeleton className="h-64" />
        </div>
      ) : favorites.isError ? (
        <ErrorState description="Seçilmişləri yükləmək mümkün olmadı." onRetry={() => void favorites.refetch()} />
      ) : favorites.data.items.length === 0 ? (
        <EmptyState
          title="Hələ heç nə seçməmisiniz."
          description="Bəyəndiyiniz elanı ürək düyməsi ilə yadda saxlaya bilərsiniz."
        />
      ) : (
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4">
          {favorites.data.items.map((listing) => (
            <ListingCard
              key={listing.shortId}
              listing={listing}
              onToggleFavorite={(item) => remove.mutate(item.shortId)}
            />
          ))}
        </ul>
      )}
    </div>
  )
}
