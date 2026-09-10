import { NavLink } from 'react-router'

import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { useStaticPages } from '@/features/catalog/hooks'

export function GuidePage() {
  const { data, isPending, isError, refetch } = useStaticPages('guide')

  return (
    <div className="mx-auto max-w-3xl py-10">
      <h1 className="text-2xl">Outdoor bələdçi</h1>
      <p className="mt-2 text-muted">Avadanlıq seçimi və outdoor təcrübəsi üzrə məqalələr.</p>

      {isPending ? (
        <div className="mt-8 space-y-3" aria-busy="true">
          {Array.from({ length: 3 }, (_, i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
      ) : null}

      {isError ? (
        <div className="mt-8">
          <ErrorState onRetry={() => void refetch()} />
        </div>
      ) : null}

      {data && data.length === 0 ? (
        <div className="mt-8">
          <EmptyState
            title="Məqalələr hazırlanır"
            description="Bələdçi məzmunu tezliklə dərc olunacaq."
          />
        </div>
      ) : null}

      {data && data.length > 0 ? (
        <ul className="mt-8 space-y-4">
          {data.map((article) => (
            <li key={article.slug} className="rounded-(--radius-card) border border-line bg-surface p-5">
              <NavLink
                to={`/beledci/${article.slug}`}
                className="font-heading text-lg font-bold text-ink hover:text-interactive"
              >
                {article.titleAz}
              </NavLink>

              {article.excerptAz ? (
                <p className="mt-2 text-sm text-muted">{article.excerptAz}</p>
              ) : null}
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  )
}
