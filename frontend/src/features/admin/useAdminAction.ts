import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'

import { ApiError } from '@/api/client'

/**
 * What happened to the last admin action, as an operator would describe it.
 *
 * `conflict` is the one that matters: two moderators reaching for the same item is the normal case
 * on a shared queue, not an error. The server answers 409 — either because the state guard refused
 * (the listing is no longer pending) or because the Version token caught an interleaved write — and
 * the operator needs to be told someone got there first and shown the refreshed queue, not handed a
 * red toast that leaves them wondering whether their click landed.
 */
export type AdminActionState =
  | { kind: 'idle' }
  | { kind: 'conflict'; message: string }
  | { kind: 'error'; message: string }
  | { kind: 'done' }

interface UseAdminActionOptions<TArgs> {
  action: (args: TArgs) => Promise<unknown>
  /** Query keys to invalidate once the action lands. Accepts the `as const` keys from adminKeys. */
  invalidate: readonly (readonly unknown[])[]
  onSuccess?: (() => void) | undefined
}

export function useAdminAction<TArgs>({ action, invalidate, onSuccess }: UseAdminActionOptions<TArgs>) {
  const queryClient = useQueryClient()
  const [state, setState] = useState<AdminActionState>({ kind: 'idle' })
  const [fieldError, setFieldError] = useState<string | undefined>(undefined)

  const mutation = useMutation({
    mutationFn: action,
    onMutate: () => {
      setState({ kind: 'idle' })
      setFieldError(undefined)
    },
    onSuccess: async () => {
      await Promise.all(invalidate.map((key) => queryClient.invalidateQueries({ queryKey: key })))

      setState({ kind: 'done' })
      onSuccess?.()
    },
    onError: async (error: unknown) => {
      if (error instanceof ApiError) {
        if (error.status === 409) {
          // Refresh first, so what the operator sees next is the state that actually won.
          await Promise.all(invalidate.map((key) => queryClient.invalidateQueries({ queryKey: key })))

          setState({
            kind: 'conflict',
            message: error.problem?.detail ?? 'Bu element artıq başqası tərəfindən emal edilib.',
          })

          return
        }

        const reason = error.fieldError('reason')

        if (reason) {
          setFieldError(reason)
          setState({ kind: 'idle' })

          return
        }

        setState({ kind: 'error', message: error.problem?.detail ?? error.message })

        return
      }

      setState({ kind: 'error', message: 'Şəbəkə xətası. Yenidən cəhd edin.' })
    },
  })

  return {
    run: mutation.mutate,
    isPending: mutation.isPending,
    state,
    fieldError,
    reset: () => {
      setState({ kind: 'idle' })
      setFieldError(undefined)
    },
  }
}
