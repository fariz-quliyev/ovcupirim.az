import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'

import { ApiError } from '@/api/client'
import { Badge } from '@/components/ui/Badge'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { formatNumber } from '@/features/listings/format'
import { getMyStore, storeKeys } from '@/features/stores/api'
import { StoreForm } from '@/features/stores/StoreForm'
import { StoreImageUploader } from '@/features/stores/StoreImageUploader'
import type { StoreOwner, StoreStatus } from '@/features/stores/types'
import { storePath } from '@/features/stores/types'

const statusLabels: Record<StoreStatus, string> = {
  PendingVerification: 'Yoxlanılır',
  Active: 'Aktiv',
  Suspended: 'Dayandırılıb',
}

/**
 * "/kabinet/magazam". One screen for the whole storefront lifecycle: the application form when the
 * seller has none, and the management panel once they do.
 */
export function MyStorePage() {
  const queryClient = useQueryClient()

  const store = useQuery({
    queryKey: storeKeys.mine,
    queryFn: getMyStore,
    retry: false,
  })

  function saved(updated: StoreOwner) {
    queryClient.setQueryData(storeKeys.mine, updated)
    void queryClient.invalidateQueries({ queryKey: ['stores'] })
  }

  if (store.isPending) {
    return <Skeleton className="h-72" />
  }

  // 404 is the ordinary answer for a seller who has not applied yet, not a failure.
  const hasNoStore = store.error instanceof ApiError && store.error.status === 404

  if (hasNoStore) {
    return (
      <div className="flex max-w-lg flex-col gap-5 py-6">
        <header className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold text-ink">Mağaza aç</h1>
          <p className="text-sm text-muted">
            Müraciətiniz yoxlamadan keçdikdən sonra mağazanız saytda görünəcək və elanlarınızı
            mağazaya bağlaya biləcəksiniz.
          </p>
        </header>

        <section className="rounded-(--radius-card) border border-line bg-surface p-4 sm:p-5">
          <StoreForm onSaved={saved} />
        </section>
      </div>
    )
  }

  if (store.isError || !store.data) {
    return <ErrorState description="Mağaza məlumatlarını yükləmək mümkün olmadı." onRetry={() => void store.refetch()} />
  }

  const data = store.data

  return (
    <div className="flex flex-col gap-6 py-6">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold text-ink">Mağazam</h1>

          {data.isPublic ? (
            <Link to={storePath(data.slug)} className="text-sm text-interactive hover:underline">
              /magaza/{data.slug}
            </Link>
          ) : (
            <p className="text-sm text-muted">/magaza/{data.slug}</p>
          )}
        </div>

        <div className="flex items-center gap-2">
          <Badge tone={data.status === 'Active' ? 'new' : 'neutral'}>{statusLabels[data.status]}</Badge>
          {data.isVerified ? <Badge tone="store">Təsdiqlənmiş</Badge> : null}
        </div>
      </header>

      {data.status === 'PendingVerification' ? (
        <p className="rounded-(--radius-card) border border-line bg-surface px-4 py-3 text-sm text-muted">
          Müraciətiniz yoxlanılır. Təsdiqlənənə qədər mağaza saytda görünmür və elanları mağazaya
          bağlamaq mümkün deyil.
        </p>
      ) : null}

      {data.status === 'Suspended' ? (
        <p className="rounded-(--radius-card) border border-line bg-surface px-4 py-3 text-sm text-muted">
          Mağaza dayandırılıb və saytda görünmür. Elanlarınız isə öz qaydası ilə saytda qalır.
          Ətraflı məlumat üçün dəstək xidməti ilə əlaqə saxlayın.
        </p>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,320px)]">
        <section className="flex flex-col gap-4 rounded-(--radius-card) border border-line bg-surface p-4 sm:p-5">
          <h2 className="text-base font-semibold text-ink">Mağaza məlumatları</h2>
          <StoreForm existing={data} onSaved={saved} />
        </section>

        <div className="flex flex-col gap-4">
          <section className="flex flex-col gap-3 rounded-(--radius-card) border border-line bg-surface p-4 sm:p-5">
            <h2 className="text-base font-semibold text-ink">Göstəricilər</h2>

            <dl className="flex flex-col gap-2 text-sm">
              <div className="flex justify-between">
                <dt className="text-muted">Aktiv elan</dt>
                <dd className="text-ink">{formatNumber(data.listingCount)}</dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-muted">İzləyici</dt>
                <dd className="text-ink">{formatNumber(data.followerCount)}</dd>
              </div>
            </dl>

            <Link to="/kabinet/elanlarim" className="text-sm text-interactive hover:underline">
              Elanlarımı idarə et
            </Link>
          </section>

          <section className="flex flex-col gap-4 rounded-(--radius-card) border border-line bg-surface p-4 sm:p-5">
            <h2 className="text-base font-semibold text-ink">Şəkillər</h2>

            <StoreImageUploader
              kind="logo"
              label="Loqo"
              hint="Kvadrat şəkil tövsiyə olunur."
              url={data.logoUrl}
              onChange={saved}
            />

            <StoreImageUploader
              kind="banner"
              label="Örtük şəkli"
              hint="Mağaza səhifəsinin başlığında göstərilir."
              url={data.bannerUrl}
              onChange={saved}
            />
          </section>
        </div>
      </div>
    </div>
  )
}
