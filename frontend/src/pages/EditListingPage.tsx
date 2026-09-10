import { useQuery } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router'

import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { useAuth } from '@/features/auth/useAuth'
import { useCategorySchema } from '@/features/catalog/hooks'
import { getListing, listingKeys } from '@/features/listings/api'
import { ListingForm } from '@/features/listings/ListingForm'
import { statusLabels } from '@/features/listings/format'

/**
 * "Düzəliş et". The same sectioned form as creation; the server decides which fields a listing in
 * this status will actually accept, and the form mirrors that by disabling the frozen ones.
 */
export function EditListingPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const { user } = useAuth()

  const listing = useQuery({
    queryKey: listingKeys.detail(id),
    queryFn: () => getListing(id),
    enabled: id.length > 0,
  })

  const schema = useCategorySchema(listing.data?.categorySlug ?? '')

  if (listing.isPending || schema.isPending) {
    return <Skeleton className="h-96" />
  }

  if (listing.isError || !listing.data) {
    return <ErrorState description="Elanı yükləmək mümkün olmadı." onRetry={() => void listing.refetch()} />
  }

  if (!listing.data.can.edit) {
    return (
      <ErrorState
        description={`Bu statusda (${statusLabels[listing.data.status]}) elana düzəliş etmək mümkün deyil.`}
      />
    )
  }

  if (schema.isError || !schema.data) {
    return <ErrorState description="Kateqoriya məlumatını yükləmək mümkün olmadı." onRetry={() => void schema.refetch()} />
  }

  return (
    <div className="flex flex-col gap-5">
      <header className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold text-ink">Elana düzəliş</h1>
        <p className="text-sm text-muted">№ {listing.data.shortId} · {listing.data.categoryNameAz}</p>
      </header>

      {listing.data.rejectionReason ? (
        <p className="rounded-(--radius-card) border border-accent/40 bg-accent/5 px-4 py-3 text-sm text-ink">
          <strong className="font-semibold">Dərc olunmama səbəbi:</strong> {listing.data.rejectionReason}
        </p>
      ) : null}

      {listing.data.status === 'Active' ? (
        <p className="rounded-(--radius-card) border border-line bg-canvas px-4 py-3 text-sm text-muted">
          Dərc olunmuş elanda yalnız təsvir, qiymət, çatdırılma və şəkillər dəyişdirilə bilər.
          Dəyişiklikdən sonra elan yenidən moderasiyaya göndərilir.
        </p>
      ) : null}

      <ListingForm
        schema={schema.data}
        existing={listing.data}
        defaultPhone={user?.phoneNumber ?? ''}
        onPublished={() => void navigate('/kabinet/elanlarim?status=pending')}
      />
    </div>
  )
}
