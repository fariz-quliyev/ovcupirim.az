import { useState } from 'react'

import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { useFaq } from '@/features/catalog/hooks'

export function HelpPage() {
  const { data, isPending, isError, refetch } = useFaq()
  const [openQuestion, setOpenQuestion] = useState<string | null>(null)

  const groups = (data ?? []).filter((group) => group.items.length > 0)

  return (
    <div className="mx-auto max-w-3xl py-10">
      <h1 className="text-2xl">Yardım</h1>
      <p className="mt-2 text-muted">Ən çox verilən suallar.</p>

      {isPending ? (
        <div className="mt-8 space-y-3" aria-busy="true">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} className="h-14" />
          ))}
        </div>
      ) : null}

      {isError ? (
        <div className="mt-8">
          <ErrorState onRetry={() => void refetch()} />
        </div>
      ) : null}

      {data && groups.length === 0 ? (
        <div className="mt-8">
          <EmptyState
            title="Suallar hazırlanır"
            description="Bu bölmə tezliklə doldurulacaq."
          />
        </div>
      ) : null}

      {groups.map((group) => (
        <section key={group.slug} className="mt-8">
          <h2 className="text-lg">{group.nameAz}</h2>

          <ul className="mt-3 divide-y divide-line rounded-(--radius-card) border border-line bg-surface">
            {group.items.map((item) => {
              const isOpen = openQuestion === item.questionAz

              return (
                <li key={item.questionAz}>
                  <button
                    type="button"
                    aria-expanded={isOpen}
                    onClick={() => setOpenQuestion(isOpen ? null : item.questionAz)}
                    className="flex w-full items-center justify-between gap-4 px-4 py-3.5 text-left text-[15px] font-medium text-ink hover:text-interactive"
                  >
                    {item.questionAz}
                    <span aria-hidden="true" className="shrink-0 text-muted">
                      {isOpen ? '−' : '+'}
                    </span>
                  </button>

                  {isOpen ? (
                    <p className="px-4 pb-4 text-sm leading-relaxed text-muted">{item.answerAz}</p>
                  ) : null}
                </li>
              )
            })}
          </ul>
        </section>
      ))}
    </div>
  )
}
