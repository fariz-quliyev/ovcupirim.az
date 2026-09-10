import { useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'

import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Select } from '@/components/ui/Select'
import { Skeleton } from '@/components/ui/Skeleton'
import { useCategoryTree } from '@/features/catalog/hooks'
import { formatNumber } from '@/features/listings/format'
import { getStoreDirectory, storeKeys } from '@/features/stores/api'
import type { StoreDirectorySort } from '@/features/stores/api'
import { StoreCard } from '@/features/stores/StoreCard'

const sorts: { value: StoreDirectorySort; label: string }[] = [
  { value: 'name', label: 'Ada görə' },
  { value: 'newest', label: 'Əvvəlcə yenilər' },
  { value: 'listings', label: 'Ən çox elan' },
  { value: 'followers', label: 'Ən çox izləyici' },
]

/**
 * "Mağazalar" — the directory of active storefronts. A storefront awaiting approval or currently
 * suspended is simply not here; the server never lists one.
 */
export function StoresPage() {
  const [params, setParams] = useSearchParams()
  const categories = useCategoryTree()

  const query = {
    category: params.get('category') ?? undefined,
    sort: (params.get('sort') as StoreDirectorySort | null) ?? undefined,
    page: Number(params.get('page') ?? '1') || 1,
  }

  const stores = useQuery({
    queryKey: storeKeys.directory(query),
    queryFn: () => getStoreDirectory(query),
  })

  function update(next: Record<string, string | null>) {
    const merged = new URLSearchParams(params)

    for (const [key, value] of Object.entries(next)) {
      if (value === null || value === '') {
        merged.delete(key)
      } else {
        merged.set(key, value)
      }
    }

    // Changing a filter starts again at the first page; paging itself must not reset it.
    if (!('page' in next)) {
      merged.delete('page')
    }

    setParams(merged)
  }

  return (
    <div className="flex flex-col gap-5">
      <header className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold text-ink">Mağazalar</h1>
        {stores.data ? (
          <p className="text-sm text-muted">{formatNumber(stores.data.total)} mağaza</p>
        ) : null}
      </header>

      <div className="flex flex-wrap items-end gap-3">
        <Select
          label="Kateqoriya"
          value={params.get('category') ?? ''}
          onChange={(event) => update({ category: event.target.value })}
        >
          <option value="">Bütün kateqoriyalar</option>
          {(categories.data ?? []).map((category) => (
            <option key={category.slug} value={category.slug}>
              {category.nameAz}
            </option>
          ))}
        </Select>

        <Select
          label="Sıralama"
          value={params.get('sort') ?? 'name'}
          onChange={(event) => update({ sort: event.target.value })}
        >
          {sorts.map((sort) => (
            <option key={sort.value} value={sort.value}>
              {sort.label}
            </option>
          ))}
        </Select>
      </div>

      {stores.isPending ? (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3" aria-busy="true">
          {Array.from({ length: 6 }, (_, index) => (
            <Skeleton key={index} className="h-20" />
          ))}
        </div>
      ) : stores.isError ? (
        <ErrorState description="Mağazaları yükləmək mümkün olmadı." onRetry={() => void stores.refetch()} />
      ) : stores.data.items.length === 0 ? (
        <EmptyState
          title="Uyğun mağaza tapılmadı."
          description="Filtri dəyişib yenidən yoxlayın."
        />
      ) : (
        <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {stores.data.items.map((store) => (
            <StoreCard key={store.slug} store={store} />
          ))}
        </ul>
      )}

      {stores.data && stores.data.totalPages > 1 ? (
        <nav aria-label="Səhifələr" className="flex items-center justify-center gap-3 pt-2">
          <Button
            type="button"
            variant="secondary"
            size="sm"
            disabled={stores.data.page <= 1}
            onClick={() => update({ page: String(stores.data!.page - 1) })}
          >
            Əvvəlki
          </Button>

          <span className="text-sm text-muted">
            {stores.data.page} / {stores.data.totalPages}
          </span>

          <Button
            type="button"
            variant="secondary"
            size="sm"
            disabled={stores.data.page >= stores.data.totalPages}
            onClick={() => update({ page: String(stores.data!.page + 1) })}
          >
            Növbəti
          </Button>
        </nav>
      ) : null}
    </div>
  )
}
