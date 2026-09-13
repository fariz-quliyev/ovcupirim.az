import { getAccessToken, setAccessToken } from '@/api/authToken'
import type { ProblemDetails } from '@/types/api'

/**
 * Where the API lives, defaulting to the same-origin path the nginx image proxies.
 *
 * An empty string has to fall back too, not just an absent value. `??` only catches
 * null/undefined, but Docker bakes a build arg in as `""` whenever the variable is declared and
 * left blank — which is exactly what .env.production.example and .env.staging.example tell the
 * operator to do for the same-origin topology. That produced `BASE_URL = ""`, turning every call
 * into `/regions` or `/listings`: the SPA fallback answered GETs with index.html (so the JSON
 * parse failed and every screen showed "could not load") and answered POSTs with 405. Caught on
 * staging, where the whole site was up and no API call worked.
 */
export function resolveBaseUrl(configured: string | undefined): string {
  return configured !== undefined && configured.trim().length > 0 ? configured : '/api/v1'
}

const BASE_URL = resolveBaseUrl(import.meta.env.VITE_API_BASE_URL)

/** An API call that came back with a non-2xx status. Carries the parsed problem document. */
export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null

  constructor(status: number, problem: ProblemDetails | null) {
    super(problem?.detail ?? problem?.title ?? `Request failed with status ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }

  /** Field-level validation messages, keyed by property name. */
  get fieldErrors(): Record<string, string[]> {
    return this.problem?.errors ?? {}
  }

  /** First message for a field, ready to render under an input. */
  fieldError(field: string): string | undefined {
    return this.fieldErrors[field]?.[0]
  }
}

interface RequestOptions extends Omit<RequestInit, 'body'> {
  body?: unknown
  /** Appended as a query string; null and undefined values are dropped. */
  query?: Record<string, string | number | boolean | null | undefined>
  /** Internal: prevents the refresh-and-retry loop from recursing. */
  skipAuthRefresh?: boolean
}

function buildUrl(path: string, query: RequestOptions['query']): string {
  const url = `${BASE_URL}${path.startsWith('/') ? path : `/${path}`}`
  if (!query) return url

  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value !== null && value !== undefined && value !== '') {
      params.append(key, String(value))
    }
  }

  const qs = params.toString()
  return qs ? `${url}?${qs}` : url
}

/**
 * The one door to the refresh endpoint.
 *
 * Refresh tokens rotate, and presenting a token that has already been rotated is indistinguishable
 * from a stolen one — the API treats it as reuse and revokes every session the account has. So two
 * refreshes that overlap do not merely waste a request: the second one logs the user out. Both
 * callers that exist — restoring a session on boot, and recovering from an expired access token —
 * share this single in-flight promise, so only one call is ever on the wire.
 *
 * The payload is returned as given so the caller can type it; this module deliberately knows
 * nothing about the shape of a user.
 */
let refreshInFlight: Promise<unknown> | null = null

export function refreshSession<T = unknown>(): Promise<T | null> {
  refreshInFlight ??= (async () => {
    try {
      const response = await fetch(buildUrl('/auth/refresh', undefined), {
        method: 'POST',
        credentials: 'include',
        headers: { Accept: 'application/json' },
      })

      if (!response.ok) {
        setAccessToken(null)
        return null
      }

      const data = (await response.json()) as { accessToken: string }
      setAccessToken(data.accessToken)
      return data
    } catch {
      setAccessToken(null)
      return null
    } finally {
      refreshInFlight = null
    }
  })()

  return refreshInFlight as Promise<T | null>
}

async function send(path: string, options: RequestOptions): Promise<Response> {
  const { body, query, headers, skipAuthRefresh: _skip, ...rest } = options
  const token = getAccessToken()

  // FormData carries its own multipart boundary, so the browser must set the header itself.
  const isFormData = body instanceof FormData

  return fetch(buildUrl(path, query), {
    ...rest,
    // The refresh token is an HttpOnly cookie, so credentials always travel.
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      ...(body === undefined || isFormData ? {} : { 'Content-Type': 'application/json' }),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers,
    },
    ...(body === undefined ? {} : { body: isFormData ? body : JSON.stringify(body) }),
  })
}

export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  let response = await send(path, options)

  // An expired access token is recoverable: refresh once, then replay the request.
  if (response.status === 401 && !options.skipAuthRefresh && getAccessToken() !== null) {
    const refreshed = await refreshSession()
    if (refreshed !== null) {
      response = await send(path, options)
    }
  }

  if (!response.ok) {
    let problem: ProblemDetails | null = null
    try {
      problem = (await response.json()) as ProblemDetails
    } catch {
      problem = null
    }
    throw new ApiError(response.status, problem)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

export const api = {
  get: <T>(path: string, options?: Omit<RequestOptions, 'body' | 'method'>) =>
    apiFetch<T>(path, { ...options, method: 'GET' }),
  post: <T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'body' | 'method'>) =>
    apiFetch<T>(path, { ...options, method: 'POST', body }),
  put: <T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'body' | 'method'>) =>
    apiFetch<T>(path, { ...options, method: 'PUT', body }),
  delete: <T>(path: string, options?: Omit<RequestOptions, 'body' | 'method'>) =>
    apiFetch<T>(path, { ...options, method: 'DELETE' }),
}
