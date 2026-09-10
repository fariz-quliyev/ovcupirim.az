import { api } from '@/api/client'
import type { HealthResponse } from '@/types/api'

export const healthKeys = {
  root: ['health'] as const,
}

export function getHealth(): Promise<HealthResponse> {
  return api.get<HealthResponse>('/health')
}
