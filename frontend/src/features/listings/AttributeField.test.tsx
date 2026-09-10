import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { renderWithProviders } from '@/features/auth/authTestUtils'
import type { AttributeDataType, AttributeSchema } from '@/features/catalog/types'

import { AttributeField } from './AttributeField'
// The component's own source, so the audit runs on what actually ships.
import attributeFieldSource from './AttributeField.tsx?raw'

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
 * One fixture covering every dataType. If a control ever needs to know *which* attribute it is
 * rendering, this suite is where that shows up.
 */
describe('AttributeField', () => {
  it('renders a text box for Text', () => {
    renderWithProviders(
      <AttributeField attribute={attribute('Text')} value="" onChange={() => {}} />,
    )

    expect(screen.getByLabelText('Nümunə')).toHaveProperty('type', 'text')
  })

  it('renders a number box for Number, carrying the schema bounds', () => {
    renderWithProviders(
      <AttributeField
        attribute={attribute('Number', { minValue: 0, maxValue: 100, decimalPlaces: 2, unit: 'kq' })}
        value=""
        onChange={() => {}}
      />,
    )

    const input = screen.getByLabelText('Nümunə, kq')

    expect(input).toHaveProperty('type', 'number')
    expect(input).toHaveAttribute('min', '0')
    expect(input).toHaveAttribute('max', '100')
    expect(input).toHaveAttribute('step', '0.01')
  })

  it('renders a checkbox for Boolean', () => {
    renderWithProviders(
      <AttributeField attribute={attribute('Boolean')} value={true} onChange={() => {}} />,
    )

    expect(screen.getByRole('checkbox', { name: 'Nümunə' })).toBeChecked()
  })

  it('renders a dropdown for Select, with the schema options', () => {
    renderWithProviders(
      <AttributeField attribute={attribute('Select', { options })} value="" onChange={() => {}} />,
    )

    expect(screen.getByRole('combobox', { name: 'Nümunə' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: '2 nəfər' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: '4 nəfər' })).toBeInTheDocument()
  })

  it('renders a checkbox group for MultiSelect', () => {
    renderWithProviders(
      <AttributeField attribute={attribute('MultiSelect', { options })} value={['2']} onChange={() => {}} />,
    )

    expect(screen.getByRole('checkbox', { name: '2 nəfər' })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: '4 nəfər' })).not.toBeChecked()
  })

  it('reports a MultiSelect value as a list, which is what the API stores', async () => {
    const onChange = vi.fn()

    renderWithProviders(
      <AttributeField attribute={attribute('MultiSelect', { options })} value={['2']} onChange={onChange} />,
    )

    await userEvent.click(screen.getByRole('checkbox', { name: '4 nəfər' }))

    expect(onChange).toHaveBeenCalledWith('sample_key', ['2', '4'])
  })

  it('marks a required field and shows the schema help text', () => {
    renderWithProviders(
      <AttributeField
        attribute={attribute('Text', { isRequired: true, helpTextAz: 'Kömək mətni', placeholderAz: 'Nümunə yazın' })}
        value=""
        onChange={() => {}}
      />,
    )

    const input = screen.getByLabelText('Nümunə *')

    expect(input).toBeRequired()
    expect(input).toHaveAttribute('placeholder', 'Nümunə yazın')
    expect(screen.getByText('Kömək mətni')).toBeInTheDocument()
  })

  it('surfaces a server field error on the field it belongs to', () => {
    renderWithProviders(
      <AttributeField attribute={attribute('Text')} value="" onChange={() => {}} error="Bu sahə tələb olunur." />,
    )

    expect(screen.getByRole('alert')).toHaveTextContent('Bu sahə tələb olunur.')
    expect(screen.getByLabelText('Nümunə')).toHaveAttribute('aria-invalid', 'true')
  })

  it('contains no attribute key or category slug, so nothing here is category-specific', () => {
    // Keys and slugs seeded in Phase 3. None of them may steer a control.
    for (const forbidden of ['capacity_person', 'lure_length', 'caliber', 'cadirlar', 'tilovlar', 'bel-cantasi']) {
      expect(attributeFieldSource).not.toContain(forbidden)
    }
  })
})
