/** Mirrors RestrictionStatus on the API. Unclassified means no policy decision is recorded yet. */
export type RestrictionStatus = 'Unrestricted' | 'Unclassified' | 'Restricted'

export type AttributeDataType = 'Text' | 'Number' | 'Boolean' | 'Select' | 'MultiSelect'

export interface CategoryNode {
  id: number
  slug: string
  nameAz: string
  nameRu: string | null
  iconKey: string | null
  imageKey: string | null
  sortOrder: number
  listingCount: number
  restrictionStatus: RestrictionStatus
  requiresAgeConfirmation: boolean
  isSelectable: boolean
  children: CategoryNode[]
}

export interface CategoryPathEntry {
  id: number
  slug: string
  nameAz: string
}

export interface CategoryDetail {
  id: number
  slug: string
  nameAz: string
  nameRu: string | null
  descriptionAz: string | null
  metaTitleAz: string | null
  metaDescriptionAz: string | null
  iconKey: string | null
  imageKey: string | null
  listingCount: number
  restrictionStatus: RestrictionStatus
  requiresAgeConfirmation: boolean
  isSelectable: boolean
  path: CategoryPathEntry[]
  children: CategoryNode[]
}

export interface AttributeOption {
  value: string
  labelAz: string
  labelRu: string | null
}

/**
 * One field of a category's effective schema. Phase 4 renders a control from `dataType` alone and
 * Phase 5 builds a filter from the same record — no category name ever reaches component logic.
 */
export interface AttributeSchema {
  key: string
  labelAz: string
  labelRu: string | null
  dataType: AttributeDataType
  unit: string | null
  isRequired: boolean
  isFilterable: boolean
  isSearchable: boolean
  minValue: number | null
  maxValue: number | null
  decimalPlaces: number | null
  maxLength: number | null
  placeholderAz: string | null
  helpTextAz: string | null
  inherited: boolean
  sortOrder: number
  options: AttributeOption[] | null
}

export interface CategorySchema {
  category: {
    id: number
    slug: string
    nameAz: string
    path: CategoryPathEntry[]
    restrictionStatus: RestrictionStatus
    requiresAgeConfirmation: boolean
    isSelectable: boolean
    isLeaf: boolean
  }
  attributes: AttributeSchema[]
}

/**
 * A location a user can pick. Flat by design: the administrative hierarchy is kept server-side and
 * never drives the picker, so a seller chooses one practical place rather than walking a tree.
 */
export interface Region {
  id: number
  slug: string
  nameAz: string
  nameRu: string | null
  listingCount: number
}

export interface StaticPage {
  slug: string
  pageType: 'Info' | 'Guide'
  titleAz: string
  bodyAz: string
  metaDescriptionAz: string | null
  excerptAz: string | null
  coverImageKey: string | null
  publishedAt: string | null
}

export interface StaticPageSummary {
  slug: string
  titleAz: string
  excerptAz: string | null
  coverImageKey: string | null
  publishedAt: string | null
}

export interface FaqItem {
  questionAz: string
  answerAz: string
}

export interface FaqCategory {
  slug: string
  nameAz: string
  items: FaqItem[]
}
