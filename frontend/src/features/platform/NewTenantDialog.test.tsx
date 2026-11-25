import { fireEvent, render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { NewTenantForm } from './NewTenantDialog'

// Form/orchestration regression only; no real Google, PostgreSQL or commercial operation.
it('asks for the pending Gmail before creation, without a UUID or an invitation link', () => {
  const create = vi.fn().mockResolvedValue(undefined)
  render(<NewTenantForm locked={false} create={create} dirty={vi.fn()} />)
  fireEvent.change(screen.getByLabelText('Nombre comercial'), { target: { value: 'Synthetic onboarding test' } })
  expect(screen.getByRole('button', { name: 'Crear botica y asignación pendiente' })).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Gmail del propietario'), { target: { value: 'future.owner+test@example.invalid' } })
  expect(create).not.toHaveBeenCalled()
  expect(screen.getByText('future.owner+test@example.invalid')).toBeInTheDocument()
  fireEvent.submit(screen.getByRole('button', { name: 'Crear botica y asignación pendiente' }).closest('form')!)
  expect(create).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Crear botica y asignación pendiente' }))
  expect(create).toHaveBeenCalledTimes(1)
  const input = create.mock.calls[0]![0] as { ownerEmail: string; status: string; startsAt: string; expiresAt: string }
  expect(input.ownerEmail).toBe('future.owner+test@example.invalid'); expect(input.status).toBe('trial')
  expect(Date.parse(input.expiresAt) - Date.parse(input.startsAt)).toBe(30 * 86400000)
  expect(screen.queryByLabelText(/Identificador interno/)).not.toBeInTheDocument()
})
it('does not submit while the original creation is locked for recovery', () => {
  const create = vi.fn()
  render(<NewTenantForm locked create={create} dirty={vi.fn()} />)
  expect(screen.getByRole('button', { name: 'Crear botica y asignación pendiente' })).toBeDisabled()
  fireEvent.submit(screen.getByRole('button', { name: 'Crear botica y asignación pendiente' }).closest('form')!)
  expect(create).not.toHaveBeenCalled()
})
