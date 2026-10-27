// @vitest-environment jsdom
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { createMemoryRouter, Link, RouterProvider, useLocation, useNavigate } from 'react-router'
import { beforeEach, expect, it } from 'vitest'
import { NavigationSafety } from './NavigationSafety'
import { useNavigationProtection } from './NavigationProtection'

const base = '/boticas/019a0162-0000-7000-8000-000000000002/sucursales/019a0162-0000-7000-8000-000000000005'
const purchase = base + '/compras'; const inventory = base + '/inventario'
beforeEach(() => sessionStorage.clear())
function mount(search = '', busyInitially = false) {
  let finish = () => {}
  function Editor() {
    const [dirty, setDirty] = React.useState(false); const [busy, setBusy] = React.useState(busyInitially)
    finish = () => setBusy(false); useNavigationProtection({ dirty, busy })
    const navigate = useNavigate()
    return <><button onClick={() => setDirty(true)}>Editar línea local</button><button onClick={() => navigate(purchase, { replace: true })}>Conciliar sin escritura</button></>
  }
  function Content() {
    const location = useLocation()
    return <><output data-testid="path">{location.pathname + location.search}</output><Link to={inventory}>Ir a inventario</Link><Link to={purchase}>Ir a compras sin parámetros</Link>{location.pathname === purchase && <Editor />}</>
  }
  const router = createMemoryRouter([{ path: '*', element: <NavigationSafety identity="synthetic-owner"><Content /></NavigationSafety> }], { initialEntries: [purchase + search] })
  const view = render(<RouterProvider router={router} />)
  return { router, ...view, finish: () => finish() }
}
import * as React from 'react'
it('protects unsaved lines until explicit discard without executing any business request', async () => {
  mount(); fireEvent.click(screen.getByText('Editar línea local')); fireEvent.click(screen.getByText('Ir a inventario'))
  expect(await screen.findByRole('heading', { name: 'Antes de salir' })).toBeInTheDocument()
  expect(screen.getByTestId('path')).toHaveTextContent(purchase)
  fireEvent.click(screen.getByRole('button', { name: 'Permanecer aquí' })); expect(screen.getByTestId('path')).toHaveTextContent(purchase)
  fireEvent.click(screen.getByText('Ir a inventario')); fireEvent.click(screen.getByRole('button', { name: 'Descartar cambios y salir' }))
  await waitFor(() => expect(screen.getByTestId('path')).toHaveTextContent(inventory))
})
it('does not permit navigation while a request is active and unblocks when it finishes', async () => {
  const view = mount('', true); fireEvent.click(screen.getByText('Ir a inventario'))
  expect(await screen.findByRole('button', { name: 'Descartar cambios y salir' })).toBeDisabled()
  act(() => view.finish()); await waitFor(() => expect(screen.getByRole('button', { name: 'Descartar cambios y salir' })).toBeEnabled())
  fireEvent.click(screen.getByRole('button', { name: 'Descartar cambios y salir' })); await waitFor(() => expect(screen.getByTestId('path')).toHaveTextContent(inventory))
})
it('preserves the original pending address through navigation and a reload on the same module', async () => {
  const search = '?receiptPending=lines&purchase=019a0162-0000-7000-8000-000000000020'
  const view = mount(search); fireEvent.click(screen.getByText('Ir a compras sin parámetros'))
  fireEvent.click(await screen.findByRole('button', { name: 'Salir conservando recuperación' }))
  const recovery = await screen.findByRole('link', { name: 'Volver a la operación original sin reenviar' }); expect(recovery).toHaveAttribute('href', purchase + search)
  view.unmount(); mount(); expect(await screen.findByRole('link', { name: 'Volver a la operación original sin reenviar' })).toHaveAttribute('href', purchase + search)
})
it('removes the pointer only when the original module clears its marker with replace', async () => {
  mount('?receiptPending=lines'); fireEvent.click(screen.getByText('Conciliar sin escritura'))
  await waitFor(() => expect(JSON.parse(sessionStorage.getItem('medipos-operation-addresses-v1/synthetic-owner')!)).toEqual([]))
})
it('retains an invitation intention instead of treating it as a disposable filter', async () => {
  const search = '?invitationRequest=019a0162-0000-7000-8000-000000000021&invitationAction=create'
  mount(search); fireEvent.click(screen.getByText('Ir a inventario'))
  fireEvent.click(await screen.findByRole('button', { name: 'Salir conservando recuperación' }))
  expect(await screen.findByRole('link', { name: 'Volver a la operación original sin reenviar' })).toHaveAttribute('href', purchase + search)
})
