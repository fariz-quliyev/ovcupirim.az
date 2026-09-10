interface SkeletonProps {
  className?: string
}

/** Loading placeholder. Size it to the real content so the layout does not shift. */
export function Skeleton({ className = '' }: SkeletonProps) {
  return <div className={`animate-pulse rounded-(--radius-card) bg-line/70 ${className}`} aria-hidden="true" />
}
