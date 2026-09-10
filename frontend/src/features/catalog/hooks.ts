import { useQuery } from '@tanstack/react-query'

import {
  CATALOG_STALE_TIME,
  catalogKeys,
  getCategory,
  getCategorySchema,
  getCategoryTree,
  getFaq,
  getPage,
  getPages,
  getRegions,
} from './api'

export function useCategoryTree() {
  return useQuery({
    queryKey: catalogKeys.tree,
    queryFn: getCategoryTree,
    staleTime: CATALOG_STALE_TIME,
  })
}

export function useCategory(slug: string) {
  return useQuery({
    queryKey: catalogKeys.category(slug),
    queryFn: () => getCategory(slug),
    staleTime: CATALOG_STALE_TIME,
    enabled: slug.length > 0,
  })
}

/** Phase 4 and Phase 5 both consume this hook rather than hard-coding category fields. */
export function useCategorySchema(slug: string) {
  return useQuery({
    queryKey: catalogKeys.schema(slug),
    queryFn: () => getCategorySchema(slug),
    staleTime: CATALOG_STALE_TIME,
    enabled: slug.length > 0,
  })
}

export function useRegions() {
  return useQuery({
    queryKey: catalogKeys.regions,
    queryFn: getRegions,
    staleTime: CATALOG_STALE_TIME,
  })
}

export function useStaticPage(slug: string) {
  return useQuery({
    queryKey: catalogKeys.page(slug),
    queryFn: () => getPage(slug),
    staleTime: 15 * 60 * 1000,
    enabled: slug.length > 0,
  })
}

export function useStaticPages(type: 'info' | 'guide') {
  return useQuery({
    queryKey: catalogKeys.pages(type),
    queryFn: () => getPages(type),
    staleTime: 15 * 60 * 1000,
  })
}

export function useFaq() {
  return useQuery({
    queryKey: catalogKeys.faq,
    queryFn: getFaq,
    staleTime: 15 * 60 * 1000,
  })
}
