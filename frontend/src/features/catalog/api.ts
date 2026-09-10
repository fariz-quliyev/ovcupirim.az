import { api } from '@/api/client'

import type {
  CategoryDetail,
  CategoryNode,
  CategorySchema,
  FaqCategory,
  Region,
  StaticPage,
  StaticPageSummary,
} from './types'

/** Taxonomy changes rarely, so these are cached far longer than listing data. */
export const catalogKeys = {
  tree: ['catalog', 'categories'] as const,
  category: (slug: string) => ['catalog', 'category', slug] as const,
  schema: (slug: string) => ['catalog', 'schema', slug] as const,
  regions: ['catalog', 'regions'] as const,
  page: (slug: string) => ['content', 'page', slug] as const,
  pages: (type: string) => ['content', 'pages', type] as const,
  faq: ['content', 'faq'] as const,
}

export const CATALOG_STALE_TIME = 60 * 60 * 1000

export function getCategoryTree(): Promise<CategoryNode[]> {
  return api.get<CategoryNode[]>('/categories')
}

export function getCategory(slug: string): Promise<CategoryDetail> {
  return api.get<CategoryDetail>(`/categories/${slug}`)
}

/** The schema contract shared by listing creation (Phase 4) and filtering (Phase 5). */
export function getCategorySchema(slug: string): Promise<CategorySchema> {
  return api.get<CategorySchema>(`/categories/${slug}/schema`)
}

export function getRegions(): Promise<Region[]> {
  return api.get<Region[]>('/regions')
}

export function getPage(slug: string): Promise<StaticPage> {
  return api.get<StaticPage>(`/pages/${slug}`)
}

export function getPages(type: 'info' | 'guide'): Promise<StaticPageSummary[]> {
  return api.get<StaticPageSummary[]>('/pages', { query: { type } })
}

export function getFaq(): Promise<FaqCategory[]> {
  return api.get<FaqCategory[]>('/faq')
}
