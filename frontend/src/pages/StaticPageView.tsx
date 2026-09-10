import { useParams } from 'react-router'

import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { useStaticPage } from '@/features/catalog/hooks'

interface StaticPageViewProps {
  /** Fixed slug for routes that always show the same page. */
  slug?: string
}

export function StaticPageView({ slug: fixedSlug }: StaticPageViewProps) {
  const params = useParams<{ slug: string }>()
  const slug = fixedSlug ?? params.slug ?? ''
  const { data, isPending, isError, error, refetch } = useStaticPage(slug)

  const notFound = isError && (error as { status?: number } | null)?.status === 404

  return (
    <article className="mx-auto max-w-3xl py-10">
      {isPending ? (
        <div aria-busy="true">
          <Skeleton className="h-9 w-2/3" />
          <Skeleton className="mt-6 h-64 w-full" />
        </div>
      ) : null}

      {notFound ? (
        <EmptyState
          title="Səhifə tapılmadı"
          description="Axtardığınız məlumat səhifəsi mövcud deyil və ya hələ dərc olunmayıb."
        />
      ) : null}

      {isError && !notFound ? <ErrorState onRetry={() => void refetch()} /> : null}

      {data ? (
        <>
          <h1 className="text-3xl">{data.titleAz}</h1>

          {/* Body is plain text from the database; paragraphs are split rather than
              injected as HTML, so no user-supplied markup can execute. */}
          <div className="mt-6 space-y-4 text-[15px] leading-relaxed text-ink">
            {data.bodyAz.split('\n').filter((line) => line.trim().length > 0).map((line, index) => (
              <p key={index}>{line}</p>
            ))}
          </div>
        </>
      ) : null}
    </article>
  )
}
