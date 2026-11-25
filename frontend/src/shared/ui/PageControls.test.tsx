import { fireEvent, render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { PageControls } from './PageControls'

it.each([0, 4, undefined])('hides pagination for a first page without an available next page (%s)', count => {
  const change = vi.fn()
  render(<PageControls offset={0} count={count} busy={false} change={change} />)
  expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  expect(screen.queryByRole('button')).not.toBeInTheDocument()
  expect(change).not.toHaveBeenCalled()
})

it('keeps a path back from the last or failed page and never advances until a full page', () => {
  const change = vi.fn()
  const view = render(<PageControls offset={20} count={undefined} busy={false} change={change} />)
  expect(screen.getByRole('navigation', { name: 'Paginación' })).toHaveTextContent('Página 2')
  expect(screen.queryByRole('button', { name: 'Página siguiente' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Página anterior' }))
  expect(change).toHaveBeenLastCalledWith(0)
  view.rerender(<PageControls offset={0} count={20} busy change={change} />)
  expect(screen.getByRole('button', { name: 'Página siguiente' })).toBeDisabled()
  view.rerender(<PageControls offset={0} count={20} busy={false} change={change} />)
  fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }))
  expect(change).toHaveBeenLastCalledWith(20)
  expect(screen.queryByRole('button', { name: 'Actualizar consulta' })).not.toBeInTheDocument()
})
