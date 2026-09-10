import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'

import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { getStore, storeKeys } from '@/features/stores/api'
import { StoreHeader } from '@/features/stores/StoreHeader'
import { StoreListingGrid } from '@/features/stores/StoreListingGrid'

/**
 * "/magaza/:slug". Reachable only while the storefront is active — a pending application and a
 * suspended store both answer 404, so a slug cannot be probed for someone's application status.
 */
export function StorePage() {
  const { slug } = useParams()

  const store = useQuery({
    queryKey: storeKeys.public(slug ?? ''),
    queryFn: () => getStore(slug!),
    enabled: Boolean(slug),
    retry: false,
  })

  if (store.isPending) {
    return <Skeleton className="h-72" />
  }

  if (store.isError || !store.data) {
    return (
      <div className="flex flex-col items-center gap-4">
        <ErrorState title="Mağaza tapılmadı" description="Belə mağaza mövcud deyil." />
        <Link to="/magazalar" className="text-sm text-interactive hover:underline">
          Bütün mağazalar
        </Link>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <nav aria-label="Naviqasiya" className="text-sm text-muted">
        <Link to="/magazalar" className="hover:underline">
          Mağazalar
        </Link>
        <span> · {store.data.name}</span>
      </nav>

      <StoreHeader store={store.data} />
      <StoreListingGrid slug={store.data.slug} />
    </div>
  )
}
