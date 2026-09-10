import { EmptyState } from '@/components/ui/EmptyState'

interface PlaceholderPageProps {
  title: string
  /** Roadmap phase that builds this page, so the scaffold never pretends to be finished. */
  phase: string
}

export function PlaceholderPage({ title, phase }: PlaceholderPageProps) {
  return (
    <div className="py-10">
      <h1 className="mb-6 text-2xl">{title}</h1>
      <EmptyState title="Bu səhifə hazırlanır" description={`Roadmap: ${phase}`} />
    </div>
  )
}
