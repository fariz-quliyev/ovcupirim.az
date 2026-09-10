import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { renderWithProviders } from '@/features/auth/authTestUtils'
import type { AttributeDataType, AttributeSchema } from '@/features/catalog/types'

import { AttributeFilter } from './AttributeFilter'
// The component's own source, so the audit runs on what actually ships.
import attributeFilterSource from './AttributeFilter.tsx?raw'

function attribute(dataType: AttributeDataType, overrides: Partial<AttributeSchema> = {}): AttributeSchema {
  return {
    key: 'sample_key',
    labelAz: 'Nümunə',
    labelRu: null,
    dataType,
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

const options = [
  { value: '2', labelAz: '2 nəfər', labelRu: null },
  { value: '4', labelAz: '4 nəfər', labelRu: null },
]

/**
 * The filter mirror of AttributeField: the control comes from the schema alone, and the parameter
 * names it writes are the ones the API reads.
 */
describe('AttributeFilter', () => {
  it('renders a range for Number and names both bounds', async () => {
    const onChange = vi.fn()

    renderWithProviders(
      <AttributeFilter
        attribute={attribute('Number', { unit: 'kq', minValue: 0, maxValue: 100 })}
        values={{}}
        onChange={onChange}
      />,
    )

    expect(screen.getByText('Nümunə, kq')).toBeInTheDocument()

    await userEvent.type(screen.getByLabelText('min.'), '5')

    expect(onChange).toHaveBeenCalledWith({ 'attr.sample_key_min': '5' })
  })

  it('renders a single toggle for Boolean', async () => {
    const onChange = vi.fn()

    renderWithProviders(
      <AttributeFilter attribute={attribute('Boolean')} values={{}} onChange={onChange} />,
    )

    await userEvent.click(screen.getByRole('checkbox', { name: 'Nümunə' }))

    expect(onChange).toHaveBeenCalledWith({ 'attr.sample_key': 'true' })
  })

  it('renders a multi-choice group for Select, because a buyer may want either', async () => {
    const onChange = vi.fn()

    renderWithProviders(
      <AttributeFilter attribute={attribute('Select', { options })} values={{}} onChange={onChange} />,
    )

    await userEvent.click(screen.getByRole('checkbox', { name: '2 nəfər' }))

    expect(onChange).toHaveBeenCalledWith({ 'attr.sample_key': '2' })
  })

  it('joins several chosen options into one any-of parameter', async () => {
    const onChange = vi.fn()

    renderWithProviders(
      <AttributeFilter
        attribute={attribute('MultiSelect', { options })}
        values={{ 'attr.sample_key': '2' }}
        onChange={onChange}
      />,
    )

    await userEvent.click(screen.getByRole('checkbox', { name: '4 nəfər' }))

    expect(onChange).toHaveBeenCalledWith({ 'attr.sample_key': '2,4' })
  })

  it('clears the parameter when the last option is unticked', async () => {
    const onChange = vi.fn()

    renderWithProviders(
      <AttributeFilter
        attribute={attribute('Select', { options })}
        values={{ 'attr.sample_key': '2' }}
        onChange={onChange}
      />,
    )

    await userEvent.click(screen.getByRole('checkbox', { name: '2 nəfər' }))

    expect(onChange).toHaveBeenCalledWith({ 'attr.sample_key': null })
  })

  it('collapses a long option list into a dropdown', () => {
    const many = Array.from({ length: 12 }, (_, index) => ({
      value: String(index),
      label: `Seçim ${index}`,
      labelAz: `Seçim ${index}`,
      labelRu: null,
    }))

    renderWithProviders(
      <AttributeFilter
        attribute={attribute('Select', { options: many })}
        values={{}}
        onChange={() => {}}
      />,
    )

    expect(screen.getByRole('combobox', { name: 'Nümunə' })).toBeInTheDocument()
  })

  it('renders nothing for a field the schema does not mark filterable', () => {
    const { container } = renderWithProviders(
      <AttributeFilter
        attribute={attribute('Text', { isFilterable: false })}
        values={{}}
        onChange={() => {}}
      />,
    )

    expect(container).toBeEmptyDOMElement()
  })

  it('renders nothing for free Text, which is served by the search box instead', () => {
    const { container } = renderWithProviders(
      <AttributeFilter attribute={attribute('Text')} values={{}} onChange={() => {}} />,
    )

    expect(container).toBeEmptyDOMElement()
  })

  it('contains no attribute key or category slug, so nothing here is category-specific', () => {
    for (const forbidden of ['capacity_person', 'lure_length', 'caliber', 'cadirlar', 'tilovlar', 'bel-cantasi']) {
      expect(attributeFilterSource).not.toContain(forbidden)
    }
  })
})
