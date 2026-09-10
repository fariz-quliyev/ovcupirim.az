import { describe, expect, it } from 'vitest'

import {
  controlForAttribute,
  filterForAttribute,
  filterParamNames,
  orderedAttributes,
} from './attributeControls'
import type { AttributeSchema } from './types'

function attr(overrides: Partial<AttributeSchema>): AttributeSchema {
  return {
    key: 'k',
    labelAz: 'Label',
    labelRu: null,
    dataType: 'Text',
    unit: null,
    isRequired: false,
    isFilterable: true,
    isSearchable: false,
    minValue: null,
    maxValue: null,
    decimalPlaces: null,
    maxLength: null,
    placeholderAz: null,
    helpTextAz: null,
    inherited: false,
    sortOrder: 10,
    options: null,
    ...overrides,
  }
}

describe('controlForAttribute', () => {
  it.each([
    ['Text', 'text'],
    ['Number', 'number'],
    ['Boolean', 'checkbox'],
    ['Select', 'dropdown'],
    ['MultiSelect', 'checkbox-group'],
  ] as const)('maps %s to the %s control', (dataType, expected) => {
    expect(controlForAttribute(attr({ dataType }))).toBe(expected)
  })

  it('never needs the category to decide', () => {
    // The whole point of the schema contract: the same field renders identically
    // regardless of which category it came from.
    const fromRods = attr({ key: 'length', dataType: 'Number' })
    const fromBoats = attr({ key: 'length', dataType: 'Number' })

    expect(controlForAttribute(fromRods)).toBe(controlForAttribute(fromBoats))
  })
})

describe('filterForAttribute', () => {
  it('turns a number into a range filter', () => {
    expect(filterForAttribute(attr({ dataType: 'Number' }))).toBe('range')
  })

  it('turns select and multi-select into option filters', () => {
    expect(filterForAttribute(attr({ dataType: 'Select' }))).toBe('options')
    expect(filterForAttribute(attr({ dataType: 'MultiSelect' }))).toBe('options')
  })

  it('excludes text and anything not marked filterable', () => {
    expect(filterForAttribute(attr({ dataType: 'Text' }))).toBe('none')
    expect(filterForAttribute(attr({ dataType: 'Number', isFilterable: false }))).toBe('none')
  })
})

describe('filterParamNames', () => {
  it('namespaces dynamic attributes under attr.', () => {
    expect(filterParamNames(attr({ key: 'material', dataType: 'Select' }))).toEqual(['attr.material'])
  })

  it('gives a number two bounds', () => {
    expect(filterParamNames(attr({ key: 'length', dataType: 'Number' }))).toEqual([
      'attr.length_min',
      'attr.length_max',
    ])
  })

  it('contributes nothing when the field is not filterable', () => {
    expect(filterParamNames(attr({ dataType: 'Text' }))).toEqual([])
  })
})

describe('orderedAttributes', () => {
  it('sorts by sort order, required first within a tie', () => {
    const result = orderedAttributes([
      attr({ key: 'c', sortOrder: 30 }),
      attr({ key: 'a', sortOrder: 10 }),
      attr({ key: 'b-required', sortOrder: 10, isRequired: true }),
    ])

    expect(result.map((a) => a.key)).toEqual(['b-required', 'a', 'c'])
  })
})
