import { Button } from '@/components/ui/Button'

interface ErrorStateProps {
  title?: string
  description?: string
  onRetry?: () => void
}

/** Every failed request gets this: a plain explanation and a way to try again. */
export function ErrorState({
  title = 'Nəsə səhv getdi',
  description = 'Məlumatı yükləmək mümkün olmadı. Bir azdan yenidən cəhd edin.',
  onRetry,
}: ErrorStateProps) {
  return (
    <div
      role="alert"
      className="flex flex-col items-center justify-center gap-3 rounded-(--radius-card) border border-line bg-surface px-6 py-14 text-center"
    >
      <h3 className="text-lg text-ink">{title}</h3>
      <p className="max-w-md text-sm text-muted">{description}</p>
      {onRetry ? (
        <Button variant="secondary" size="sm" onClick={onRetry}>
          Yenidən cəhd et
        </Button>
      ) : null}
    </div>
  )
}
