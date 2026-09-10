import { NavLink } from 'react-router'

import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { useCategoryTree } from '@/features/catalog/hooks'

export function CategoriesPage() {
  const { data, isPending, isError, refetch } = useCategoryTree()

  return (
    <div className="py-10">
      <h1 className="text-2xl">Kateqoriyalar</h1>
      <p className="mt-2 text-muted">Ov, balıqçılıq, kamp və outdoor avadanlıqları üzrə bütün bölmələr.</p>

      {isPending ? (
        <div className="mt-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-3" aria-busy="true">
          {Array.from({ length: 6 }, (_, i) => (
            <Skeleton key={i} className="h-48" />
          ))}
        </div>
      ) : null}

      {isError ? (
        <div className="mt-8">
          <ErrorState
            title="Kateqoriyaları yükləmək mümkün olmadı"
            onRetry={() => void refetch()}
          />
        </div>
      ) : null}

      {data && data.length === 0 ? (
        <div className="mt-8">
          <EmptyState title="Kateqoriya yoxdur" description="Kateqoriya siyahısı hələ hazırlanmayıb." />
        </div>
      ) : null}

      {data && data.length > 0 ? (
        <div className="mt-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {data.map((category) => (
            <section
              key={category.slug}
              className="rounded-(--radius-card) border border-line bg-surface p-5"
            >
              <div className="flex items-baseline justify-between gap-2">
                <NavLink
                  to={`/elanlar/${category.slug}`}
                  className="font-heading text-lg font-bold text-ink hover:text-interactive"
                >
                  {category.nameAz}
                </NavLink>

                {category.requiresAgeConfirmation ? (
                  <span
                    className="shrink-0 rounded bg-canvas px-2 py-0.5 text-xs text-muted"
                    title="Bu kateqoriya üçün təsnifat gözlənilir"
                  >
                    18+
                  </span>
                ) : null}
              </div>

              <ul className="mt-3 space-y-1.5">
                {category.children.map((child) => (
                  <li key={child.slug}>
                    <NavLink
                      to={`/elanlar/${category.slug}/${child.slug}`}
                      className="text-sm text-muted hover:text-interactive"
                    >
                      {child.nameAz}
                    </NavLink>
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      ) : null}
    </div>
  )
}
