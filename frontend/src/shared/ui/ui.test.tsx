import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Button, IconButton } from './Button'
import { SearchField, TextField } from './TextField'
import { Steps } from './Steps'
import { ErrorState, LoadingState } from './States'

describe('accessible UI primitives', () => {
  it('does not submit a parent form and blocks repeated actions while pending', async () => {
    const submit = vi.fn(event => event.preventDefault())
    const action = vi.fn()
    const view = render(<form onSubmit={submit}><Button onClick={action}>Continuar</Button></form>)
    await userEvent.click(screen.getByRole('button', { name: 'Continuar' }))
    expect(action).toHaveBeenCalledTimes(1)
    expect(submit).not.toHaveBeenCalled()
    view.rerender(<form onSubmit={submit}><Button pending onClick={action}>Continuar</Button></form>)
    await userEvent.click(screen.getByRole('button', { name: 'Continuar' }))
    expect(action).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button')).toHaveAttribute('aria-busy', 'true')
  })
  it('names icon actions and supports keyboard activation', async () => {
    const action = vi.fn()
    render(<IconButton label="Cerrar panel" onClick={action}><span aria-hidden="true">×</span></IconButton>)
    const user = userEvent.setup()
    await user.tab()
    expect(screen.getByRole('button', { name: 'Cerrar panel' })).toHaveFocus()
    await user.keyboard('{Enter}')
    expect(action).toHaveBeenCalledOnce()
  })
  it('associates unique input labels, help and errors without losing caller descriptions', () => {
    render(<><p id="context">Sucursal actual</p><TextField label="Código" hint="Sin espacios" error="Código requerido" aria-describedby="context" /><TextField label="Nombre" hint="Nombre visible" /></>)
    const code = screen.getByRole('textbox', { name: 'Código' })
    expect(code).toHaveAttribute('aria-invalid', 'true')
    expect(code).toHaveAccessibleDescription('Sucursal actual Sin espacios Código requerido')
    expect(code.id).not.toBe(screen.getByRole('textbox', { name: 'Nombre' }).id)
    expect(screen.getByRole('alert')).toHaveTextContent('Código requerido')
  })
  it('explains an unavailable search without accepting input', () => {
    render(<SearchField label="Buscar" disabled hint="Disponible en F2" />)
    expect(screen.getByRole('searchbox', { name: 'Buscar' })).toBeDisabled()
    expect(screen.getByRole('searchbox')).toHaveAccessibleDescription('Disponible en F2')
  })
  it('announces progress and loading, and keeps recovery keyboard accessible', async () => {
    const retry = vi.fn()
    render(<><Steps labels={['Botica', 'Sucursal', 'Tu espacio']} current={1} /><LoadingState label="Verificando acceso" /><ErrorState message="Conexión interrumpida" retry={retry} /></>)
    expect(screen.getByText('Sucursal')).toHaveAttribute('aria-current', 'step')
    expect(screen.getByRole('status')).toHaveTextContent('Verificando acceso')
    expect(screen.getByRole('alert')).toHaveTextContent('Conexión interrumpida')
    await userEvent.click(screen.getByRole('button', { name: 'Volver a intentar' }))
    expect(retry).toHaveBeenCalledOnce()
  })
})
