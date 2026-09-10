import { useState } from 'react'
import { useNavigate } from 'react-router'

import { Button } from '@/components/ui/Button'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { useAuth } from '@/features/auth/useAuth'
import { CategoryPicker } from '@/features/catalog/CategoryPicker'
import { useCategorySchema } from '@/features/catalog/hooks'
import { ListingForm } from '@/features/listings/ListingForm'

/**
 * Category first, then one sectioned form — the shape Tap.az uses. The category is fixed once
 * chosen, because the whole attribute schema hangs off it.
 */
export function CreateListingPage() {
  const navigate = useNavigate()
  const { user } = useAuth()
  const [categorySlug, setCategorySlug] = useState<string | null>(null)
  const schema = useCategorySchema(categorySlug ?? '')

  if (!categorySlug) {
    return (
      <div className="flex flex-col gap-5">
        <header className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold text-ink">Yeni elan</h1>
          <p className="text-muted">Başlamaq üçün kateqoriya seçin.</p>
        </header>

        <CategoryPicker onSelect={(category) => setCategorySlug(category.slug)} />
      </div>
    )
  }

  if (schema.isPending) {
    return <Skeleton className="h-96" />
  }

  if (schema.isError || !schema.data) {
    return <ErrorState description="Kateqoriya məlumatını yükləmək mümkün olmadı." onRetry={() => void schema.refetch()} />
  }

  const path = schema.data.category.path.map((entry) => entry.nameAz).join(' › ')

  return (
    <div className="flex flex-col gap-5">
      <header className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold text-ink">Yeni elan</h1>

        <div className="flex flex-wrap items-center gap-2 text-sm text-muted">
          <span>
            {path ? `${path} › ` : ''}
            {schema.data.category.nameAz}
          </span>

          <Button type="button" variant="ghost" size="sm" onClick={() => setCategorySlug(null)}>
            Kateqoriyanı dəyiş
          </Button>
        </div>
      </header>

      <ListingForm
        schema={schema.data}
        defaultPhone={user?.phoneNumber ?? ''}
        onPublished={() => void navigate('/kabinet/elanlarim?status=pending')}
      />
    </div>
  )
}
