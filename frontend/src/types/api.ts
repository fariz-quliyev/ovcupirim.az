/** RFC 7807 problem document — the shape every API error arrives in. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  errors?: Record<string, string[]>
}

/** Envelope returned by every list endpoint. */
export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
  totalPages: number
}

export interface HealthResponse {
  status: string
  service: string
  utc: string
}
