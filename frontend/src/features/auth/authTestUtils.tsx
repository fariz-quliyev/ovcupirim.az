import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router'
import type { Mock } from 'vitest'
import { vi } from 'vitest'

import { AuthProvider } from './AuthProvider'
import type { AuthResponse, User } from './types'

export const testUser: User = {
  id: '11111111-1111-1111-1111-111111111111',
  phoneNumber: '+994501234567',
  fullName: 'Test İstifadəçi',
  email: null,
  role: 'User',
  isPhoneVerified: true,
  createdAt: '2026-08-29T12:00:00+00:00',
}

export const testAuthResponse: AuthResponse = {
  accessToken: 'test-access-token',
  expiresInSeconds: 900,
  user: testUser,
}

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

export function problemResponse(status: number, detail: string, errors?: Record<string, string[]>): Response {
  return new Response(
    JSON.stringify({ status, title: 'Error', detail, ...(errors ? { errors } : {}) }),
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  )
}

/**
 * A response that stays pending until the test releases it.
 *
 * Loading states are only observable while a request is in flight, but a stub that never settles
 * leaves the client's shared refresh promise permanently occupied — refreshes are de-duplicated on
 * purpose, so one abandoned call would wedge every later test in the file. Releasing it before the
 * test ends keeps the loading assertion and the clean-up.
 */
export function deferredResponse(): { promise: Promise<Response>; release: (response: Response) => void } {
  let release: (response: Response) => void = () => {}
  const promise = new Promise<Response>((resolve) => {
    release = resolve
  })

  return { promise, release }
}

/**
 * Routes a stubbed fetch by URL fragment rather than by call order. Requests fire concurrently
 * (the auth boot refresh alongside page queries), so order-based stubbing is unreliable.
 */
export function mockFetchByUrl(routes: Record<string, () => Response>): ReturnType<typeof vi.fn> {
  return vi.fn((input: RequestInfo | URL) => {
    const url = String(typeof input === 'string' ? input : input instanceof URL ? input.href : input.url)
    const match = Object.keys(routes).find((fragment) => url.includes(fragment))

    if (!match) {
      return Promise.resolve(problemResponse(404, `No stub for ${url}`))
    }

    return Promise.resolve(routes[match]!())
  })
}

interface FetchCall {
  url: string
  headers: Record<string, string>
  body: unknown
}

/** Reads one recorded fetch call, failing loudly instead of returning undefined. */
export function fetchCall(mock: Mock, index: number): FetchCall {
  const call = mock.mock.calls[index]

  if (!call) {
    throw new Error(`Expected a fetch call at index ${index}, but only ${mock.mock.calls.length} were made.`)
  }

  const [url, init] = call as [RequestInfo, RequestInit | undefined]
  const rawBody = init?.body

  return {
    url: String(url),
    headers: (init?.headers ?? {}) as Record<string, string>,
    body: typeof rawBody === 'string' ? JSON.parse(rawBody) : rawBody,
  }
}

/** Renders inside the real providers so auth state behaves as it does in the app. */
export function renderWithProviders(ui: ReactNode, { route = '/' }: { route?: string } = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>
        <AuthProvider>{ui}</AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}
