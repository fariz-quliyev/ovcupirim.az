import { QueryClient } from '@tanstack/react-query'

import { ApiError } from '@/api/client'

/** Server-state defaults from the plan: taxonomy is cached far longer than listings. */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 60_000,
      gcTime: 5 * 60_000,
      refetchOnWindowFocus: false,
      retry: (failureCount, error) => {
        // A 4xx will not fix itself — only retry infrastructure failures.
        if (error instanceof ApiError && error.status < 500) return false
        return failureCount < 2
      },
    },
  },
})
