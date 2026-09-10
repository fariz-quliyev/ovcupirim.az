import type { ReactNode } from 'react'

import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { testIds } from '@/testIds'

export interface QueueColumn<T> {
  key: string
  header: string
  render: (row: T) => ReactNode
  /** Hidden below the medium breakpoint, for columns an operator can work without on a phone. */
  secondary?: boolean
  align?: 'left' | 'right'
}

interface QueueTableProps<T> {
  columns: QueueColumn<T>[]
  rows: T[] | undefined
  rowKey: (row: T) => string
  isPending: boolean
  isError: boolean
  onRetry: () => void
  emptyTitle: string
  emptyDescription?: string
  errorDescription: string
  /** Marks the open row when the table drives a detail pane. */
  selectedKey?: string | undefined
  onSelect?: ((row: T) => void) | undefined
}

/**
 * The one table every admin queue renders through.
 *
 * Loading, empty and error are states of the same component rather than three ad-hoc branches per
 * screen, so eight queues cannot drift into eight different ideas of what "no results" looks like.
 * Wide content scrolls inside the table's own container; the page body never scrolls sideways.
 */
export function QueueTable<T>({
  columns,
  rows,
  rowKey,
  isPending,
  isError,
  onRetry,
  emptyTitle,
  emptyDescription,
  errorDescription,
  selectedKey,
  onSelect,
}: QueueTableProps<T>) {
  if (isPending) {
    return (
      <div className="flex flex-col gap-2" aria-busy="true" data-testid={testIds.queueLoading}>
        {Array.from({ length: 6 }, (_, index) => (
          <Skeleton key={index} className="h-12" />
        ))}
      </div>
    )
  }

  if (isError) {
    return <ErrorState description={errorDescription} onRetry={onRetry} />
  }

  if (!rows || rows.length === 0) {
    return (
      <div data-testid={testIds.queueEmpty}>
        <EmptyState title={emptyTitle} {...(emptyDescription ? { description: emptyDescription } : {})} />
      </div>
    )
  }

  return (
    <div className="overflow-x-auto rounded-(--radius-card) border border-line bg-surface">
      <table className="w-full min-w-[36rem] border-collapse text-sm" data-testid={testIds.queueTable}>
        <thead>
          <tr className="border-b border-line text-left text-xs font-semibold uppercase tracking-wide text-muted">
            {columns.map((column) => (
              <th
                key={column.key}
                scope="col"
                className={`px-3 py-2.5 ${column.align === 'right' ? 'text-right' : ''} ${
                  column.secondary ? 'hidden md:table-cell' : ''
                }`}
              >
                {column.header}
              </th>
            ))}
            {onSelect ? <th scope="col" className="w-px px-3 py-2.5" /> : null}
          </tr>
        </thead>

        <tbody>
          {rows.map((row) => {
            const key = rowKey(row)
            const isSelected = selectedKey === key

            return (
              <tr
                key={key}
                data-testid={testIds.queueRow}
                aria-current={isSelected ? 'true' : undefined}
                className={`border-b border-line/70 last:border-0 ${
                  isSelected ? 'bg-interactive-soft' : 'hover:bg-canvas'
                }`}
              >
                {columns.map((column) => (
                  <td
                    key={column.key}
                    className={`px-3 py-2.5 align-top ${column.align === 'right' ? 'text-right' : ''} ${
                      column.secondary ? 'hidden md:table-cell' : ''
                    }`}
                  >
                    {column.render(row)}
                  </td>
                ))}

                {onSelect ? (
                  <td className="px-3 py-2.5 text-right">
                    <Button type="button" variant="secondary" size="sm" onClick={() => onSelect(row)}>
                      Aç
                    </Button>
                  </td>
                ) : null}
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
