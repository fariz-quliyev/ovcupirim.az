import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Button } from '@/components/ui/Button'

describe('Button', () => {
  it('renders its label', () => {
    render(<Button>Yeni elan</Button>)

    expect(screen.getByRole('button', { name: 'Yeni elan' })).toBeInTheDocument()
  })

  it('calls onClick when pressed', async () => {
    const onClick = vi.fn()
    render(<Button onClick={onClick}>Axtar</Button>)

    await userEvent.click(screen.getByRole('button', { name: 'Axtar' }))

    expect(onClick).toHaveBeenCalledOnce()
  })

  it('does not fire when disabled', async () => {
    const onClick = vi.fn()
    render(
      <Button onClick={onClick} disabled>
        Axtar
      </Button>,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Axtar' }))

    expect(onClick).not.toHaveBeenCalled()
  })
})
