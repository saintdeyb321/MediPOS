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
  expect(create.mock.calls[0]![0]).toEqual({
    tradingName: 'Synthetic onboarding test', ownerEmail: 'future.owner+test@example.invalid',
    startsAt: expect.stringMatching(/Z$/), maxBranches: 2, termMonths: 0,
  })
  expect(screen.getByText(/Prueba de 7 días/)).toBeInTheDocument()
  expect(screen.queryByLabelText(/Identificador interno/)).not.toBeInTheDocument()
})
it('submits the chosen calendar period, without a client-calculated date', () => {
  const create = vi.fn().mockResolvedValue(undefined)
  render(<NewTenantForm locked={false} create={create} dirty={vi.fn()} />)
  fireEvent.change(screen.getByLabelText('Nombre comercial'), { target: { value: 'Synthetic botica' } })
  fireEvent.change(screen.getByLabelText('Gmail del propietario'), { target: { value: 'owner@example.invalid' } })
  fireEvent.change(screen.getByRole('combobox', { name: 'Plan de licencia' }), { target: { value: '12' } })
  fireEvent.click(screen.getByRole('button', { name: 'Crear botica y asignación pendiente' }))
  expect(create.mock.calls[0]![0]).toEqual({
    tradingName: 'Synthetic botica', ownerEmail: 'owner@example.invalid',
    startsAt: expect.stringMatching(/Z$/), maxBranches: 2, termMonths: 12,
  })
})

it('does not submit while the original creation is locked for recovery', () => {
  const create = vi.fn()
  render(<NewTenantForm locked create={create} dirty={vi.fn()} />)
  expect(screen.getByRole('button', { name: 'Crear botica y asignación pendiente' })).toBeDisabled()
  fireEvent.submit(screen.getByRole('button', { name: 'Crear botica y asignación pendiente' }).closest('form')!)
  expect(create).not.toHaveBeenCalled()
})
