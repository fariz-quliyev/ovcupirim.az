import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'

import { useAdminQueue } from './useAdminQueue'

/**
 * The URL is the query for every admin screen. These are the rules eight queues share, so they are
 * tested once here rather than eight times over.
 */
function Probe({ defaults }: { defaults?: Record<string, string> }) {
  const { values, update, page, goToPage, reset } = useAdminQueue(defaults)

  return (
    <div>
      <output data-testid="values">{JSON.stringify(values)}</output>
      <output data-testid="page">{page}</output>

      <button type="button" onClick={() => update({ status: 'Active' })}>
        filtrlə
      </button>
      <button type="button" onClick={() => goToPage(3)}>
        səhifə 3
      </button>
      <button type="button" onClick={() => update({ status: null })}>
        təmizlə
      </button>
      <button type="button" onClick={reset}>
        sıfırla
      </button>
    </div>
  )
}

function renderProbe(route: string, defaults?: Record<string, string>) {
  return render(
    <MemoryRouter initialEntries={[route]}>
      <Probe {...(defaults ? { defaults } : {})} />
    </MemoryRouter>,
  )
}

function values() {
  return JSON.parse(screen.getByTestId('values').textContent ?? '{}') as Record<string, string>
}

describe('useAdminQueue', () => {
  it('reads the query string, with defaults underneath', () => {
    renderProbe('/admin/stores?status=Suspended', { status: 'PendingVerification', extra: 'x' })

    expect(values()).toEqual({ status: 'Suspended', extra: 'x' })
  })

  it('defaults apply when the URL says nothing', () => {
    renderProbe('/admin/stores', { status: 'PendingVerification' })

    expect(values()['status']).toBe('PendingVerification')
  })

  it('changing a filter returns to the first page', async () => {
    renderProbe('/admin/moderation?page=4')

    expect(screen.getByTestId('page')).toHaveTextContent('4')

    await userEvent.click(screen.getByRole('button', { name: 'filtrlə' }))

    // Otherwise a narrower filter leaves the operator on a page that no longer exists.
    expect(values()['page']).toBeUndefined()
    expect(screen.getByTestId('page')).toHaveTextContent('1')
  })

  it('paging itself does not reset the filters', async () => {
    renderProbe('/admin/stores?status=Active')

    await userEvent.click(screen.getByRole('button', { name: 'səhifə 3' }))

    expect(values()).toMatchObject({ status: 'Active', page: '3' })
  })

  it('an empty value removes the parameter rather than sending a blank one', async () => {
    renderProbe('/admin/stores?status=Active')

    await userEvent.click(screen.getByRole('button', { name: 'təmizlə' }))

    expect(values()['status']).toBeUndefined()
  })

  it('reset clears everything', async () => {
    renderProbe('/admin/audit?action=store.&page=2&entityType=Store')

    await userEvent.click(screen.getByRole('button', { name: 'sıfırla' }))

    expect(values()).toEqual({})
  })

  it('a nonsense page falls back to the first', () => {
    renderProbe('/admin/reports?page=abc')

    expect(screen.getByTestId('page')).toHaveTextContent('1')
  })
})
