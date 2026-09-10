import type { AttributeSchema } from './types'

/**
 * The control a schema field maps to. Phase 4 (listing form) and Phase 5 (filter panel) both switch
 * on this rather than on category names, which is what keeps category-specific logic out of React.
 */
export type ControlKind = 'text' | 'number' | 'checkbox' | 'dropdown' | 'checkbox-group'

export function controlForAttribute(attribute: AttributeSchema): ControlKind {
  switch (attribute.dataType) {
    case 'Text':
      return 'text'
    case 'Number':
      return 'number'
    case 'Boolean':
      return 'checkbox'
    case 'Select':
      return 'dropdown'
    case 'MultiSelect':
      return 'checkbox-group'
  }
}

/** The filter control for the same field. Number becomes a range rather than a single input. */
export type FilterKind = 'none' | 'range' | 'toggle' | 'options'

export function filterForAttribute(attribute: AttributeSchema): FilterKind {
  if (!attribute.isFilterable) {
    return 'none'
  }

  switch (attribute.dataType) {
    case 'Number':
      return 'range'
    case 'Boolean':
      return 'toggle'
    case 'Select':
    case 'MultiSelect':
      return 'options'
    case 'Text':
      return 'none'
  }
}

/**
 * Query-string keys a filter contributes. Dynamic attributes are namespaced under `attr.` so they
 * never collide with the fixed filters (price, region, condition …).
 */
export function filterParamNames(attribute: AttributeSchema): string[] {
  switch (filterForAttribute(attribute)) {
    case 'range':
      return [`attr.${attribute.key}_min`, `attr.${attribute.key}_max`]
    case 'toggle':
    case 'options':
      return [`attr.${attribute.key}`]
    case 'none':
      return []
  }
}

/** Fields in display order, required ones first within the same sort order. */
export function orderedAttributes(attributes: AttributeSchema[]): AttributeSchema[] {
  return [...attributes].sort(
    (a, b) => a.sortOrder - b.sortOrder || Number(b.isRequired) - Number(a.isRequired),
  )
}
