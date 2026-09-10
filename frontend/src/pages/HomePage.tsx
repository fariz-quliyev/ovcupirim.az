import { useQuery } from '@tanstack/react-query'

import { getHealth, healthKeys } from '@/api/endpoints/health'
import { Badge } from '@/components/ui/Badge'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'

/**
 * Phase 1 home page. The full ten-section homepage arrives in Phase 6; for now this
 * proves the frontend, the API and the database are wired together end to end.
 */
export function HomePage() {
  const health = useQuery({
    queryKey: healthKeys.root,
    queryFn: getHealth,
    retry: false,
  })

  return (
    <div className="py-10">
      <section className="rounded-(--radius-card) border border-line bg-surface p-8">
        <Badge tone="new">Phase 1</Badge>
        <h1 className="mt-4 text-3xl">Ovcuprim.az</h1>
        <p className="mt-3 max-w-2xl text-muted">
          Layihənin bünövrəsi quruldu: React + Vite + TypeScript, ASP.NET Core API, PostgreSQL və verilənlər
          bazası sxemi. Növbəti mərhələ — autentifikasiya və istifadəçilər.
        </p>
      </section>

      <section className="mt-6 rounded-(--radius-card) border border-line bg-surface p-6">
        <h2 className="text-lg">API bağlantısı</h2>

        {health.isPending ? <Skeleton className="mt-4 h-16 w-full max-w-md" /> : null}

        {health.isError ? (
          <div className="mt-4">
            <ErrorState
              title="API-yə qoşulmaq mümkün olmadı"
              description="Backend işləyirmi? dotnet run --project src/Ovcuprim.Api"
              onRetry={() => void health.refetch()}
            />
          </div>
        ) : null}

        {health.isSuccess ? (
          <dl className="mt-4 grid max-w-md gap-2 text-sm">
            <div className="flex justify-between border-b border-line py-2">
              <dt className="text-muted">Status</dt>
              <dd className="font-semibold text-interactive">{health.data.status}</dd>
            </div>
            <div className="flex justify-between border-b border-line py-2">
              <dt className="text-muted">Servis</dt>
              <dd className="font-semibold">{health.data.service}</dd>
            </div>
            <div className="flex justify-between py-2">
              <dt className="text-muted">Server vaxtı</dt>
              <dd className="font-semibold">{new Date(health.data.utc).toLocaleString('az-AZ')}</dd>
            </div>
          </dl>
        ) : null}
      </section>
    </div>
  )
}
