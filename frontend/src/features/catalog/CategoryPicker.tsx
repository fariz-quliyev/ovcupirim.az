import { useState } from 'react'

import { Skeleton } from '@/components/ui/Skeleton'
import { testIds } from '@/testIds'
import { ErrorState } from '@/components/ui/ErrorState'

import { useCategoryTree } from './hooks'
import type { CategoryNode } from './types'

interface CategoryPickerProps {
  /** Called with the chosen leaf subcategory. */
  onSelect: (category: CategoryNode, parent: CategoryNode) => void
  selectedSlug?: string
}

/**
 * Two-pane category chooser: top-level on the left, subcategories on the right.
 * Built for the Phase 4 listing wizard, where step one is picking a category.
 */
export function CategoryPicker({ onSelect, selectedSlug }: CategoryPickerProps) {
  const { data, isPending, isError, refetch } = useCategoryTree()
  const [openSlug, setOpenSlug] = useState<string | null>(null)

  if (isPending) {
    return (
      <div className="grid gap-3 sm:grid-cols-2" aria-busy="true">
        <Skeleton className="h-64" />
        <Skeleton className="h-64" />
      </div>
    )
  }

  if (isError) {
    return <ErrorState onRetry={() => void refetch()} />
  }

  const open = data.find((c) => c.slug === openSlug) ?? data[0]

  return (
    <div className="grid gap-4 sm:grid-cols-2" data-testid={testIds.categoryPicker}>
      <ul className="rounded-(--radius-card) border border-line bg-surface p-1">
        {data.map((category) => (
          <li key={category.slug}>
            <button
              type="button"
              onClick={() => setOpenSlug(category.slug)}
              aria-current={open?.slug === category.slug}
              className={`w-full rounded px-3 py-2.5 text-left text-[15px] transition-colors ${
                open?.slug === category.slug
                  ? 'bg-interactive-soft font-semibold text-interactive'
                  : 'text-ink hover:bg-canvas'
              }`}
            >
              {category.nameAz}
            </button>
          </li>
        ))}
      </ul>

      <ul className="rounded-(--radius-card) border border-line bg-surface p-1">
        {(open?.children ?? []).map((child) => (
          <li key={child.slug}>
            <button
              type="button"
              disabled={!child.isSelectable}
              onClick={() => open && onSelect(child, open)}
              className={`w-full rounded px-3 py-2.5 text-left text-[15px] transition-colors disabled:cursor-not-allowed disabled:text-faint ${
                selectedSlug === child.slug
                  ? 'bg-interactive-soft font-semibold text-interactive'
                  : 'text-ink hover:bg-canvas'
              }`}
            >
              {child.nameAz}
              {child.requiresAgeConfirmation ? (
                <span className="ml-2 text-xs text-muted">18+</span>
              ) : null}
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}
