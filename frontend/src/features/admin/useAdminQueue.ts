import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router'

/**
 * The one implementation of "the URL is the query" for every admin queue.
 * </summary>
 *
 * Same rules the public catalogue follows, for the same reasons: a filtered queue is shareable
 * between operators, survives the back button, and reloads where it was. Changing a filter returns
 * to the first page; paging itself does not.
 *
 * No queue screen may keep filter state in React — that is how eight queues end up with eight
 * subtly different paging behaviours.
 */
export function useAdminQueue(defaults: Record<string, string> = {}) {
  const [params, setParams] = useSearchParams()

  const values = useMemo(() => {
    const entries: Record<string, string> = { ...defaults }

    for (const [key, value] of params.entries()) {
      entries[key] = value
    }

    return entries
    // defaults is a literal at every call site; spreading it here keeps the identity stable enough.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params])

  const update = useCallback(
    (next: Record<string, string | null>) => {
      const merged = new URLSearchParams(params)

      for (const [key, value] of Object.entries(next)) {
        if (value === null || value === '') {
          merged.delete(key)
        } else {
          merged.set(key, value)
        }
      }

      if (!('page' in next)) {
        merged.delete('page')
      }

      setParams(merged)
    },
    [params, setParams],
  )

  const page = Number(values['page'] ?? '1') || 1

  const goToPage = useCallback((next: number) => update({ page: String(next) }), [update])

  const reset = useCallback(() => setParams(new URLSearchParams()), [setParams])

  return { values, update, page, goToPage, reset }
}
