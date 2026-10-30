import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { focusManager } from '@tanstack/react-query'
import { afterEach, expect, it, vi } from 'vitest'
import { Link, MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { RuntimeProvider } from '../providers/runtime'
import { PlatformLayout } from '../layouts/PlatformLayout'
import { SelectionLayout } from '../layouts/SelectionLayout'
import { createMediPosService } from '../../features/access'
import { LoginPage, SessionGate } from '../../features/auth'
import { TenantSelection } from '../../features/tenants'
import { HttpClient } from '../../shared/api/http'
import { EntryRouter } from './EntryRouter'

// Session/transport doubles exercise routing only, never Google or persisted roles.
const userId = '019a0162-0000-7000-8000-000000000001'
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
function Location() { const location = useLocation(); return <output data-testid="path">{location.pathname}</output> }
function mount(decision: number | 'network' = 200, code = 'access.platform_denied', path = '/') {
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (route, init) => {
    if (route === '/auth/me') return json({ userId })
    if (route === '/platform/session') {
      if (decision === 'network') throw new TypeError('Synthetic network loss')
      return decision === 200 ? json({ allowed: true }) : json({ code }, decision)
    }
    if (String(route).split('?')[0] === '/api/me/tenants') return json({ items: [], nextAfterTenantId: null })
    throw new Error('Unexpected request: ' + route + String(init?.method))
  })
  const http = new HttpClient(fetcher)
  render(<RuntimeProvider runtime={{ service: createMediPosService(http), http, preview: false }}><MemoryRouter initialEntries={[path]}><Routes>
    <Route path="/" element={<LoginPage />} /><Route element={<SessionGate />}>
      <Route path="/entrada" element={<EntryRouter />} />
      <Route path="/plataforma" element={<PlatformLayout />}><Route index element={<h1>Consola autorizada de prueba</h1>} /></Route>
      <Route path="/seleccionar-botica" element={<SelectionLayout><TenantSelection /></SelectionLayout>} />
    </Route>
  </Routes><Link to="/entrada">Volver al punto de entrada</Link><Location /></MemoryRouter></RuntimeProvider>)
  return fetcher
}
afterEach(() => focusManager.setFocused(undefined))
it('routes an approved Google session to its separate layout with one admission and one logout', async () => {
  const fetcher = mount()
  await screen.findByRole('heading', { name: 'Consola autorizada de prueba' })
  expect(screen.getByTestId('path')).toHaveTextContent('/plataforma')
  expect(fetcher.mock.calls.filter(([path]) => path === '/platform/session')).toHaveLength(1)
  expect(screen.getByRole('navigation', { name: 'Navegación de plataforma' })).toBeInTheDocument()
  expect(screen.getAllByRole('button', { name: 'Cerrar sesión' })).toHaveLength(1)
  expect(screen.queryByRole('link', { name: 'MediPOS, elegir botica' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('link', { name: 'Volver al punto de entrada' }))
  await screen.findByRole('heading', { name: 'Consola autorizada de prueba' })
  expect(fetcher.mock.calls.filter(([path]) => path === '/platform/session')).toHaveLength(1)
})
it('routes an ordinary Owner/employee to selection only after an explicit platform denial', async () => {
  const fetcher = mount(403)
  await screen.findByText('Aún no tienes boticas asignadas')
  expect(screen.getByTestId('path')).toHaveTextContent('/seleccionar-botica')
  expect(screen.queryByRole('link', { name: 'Cambiar a Plataforma' })).not.toBeInTheDocument()
  expect(screen.queryByText('Mi identidad para asignar propietario')).not.toBeInTheDocument()
  expect(fetcher.mock.calls.filter(([path]) => path === '/platform/session')).toHaveLength(1)
})
it.each([[429, 'platform.rate_limited'], [503, 'service.unavailable'], ['network', ''], [403, 'auth.origin_denied']] as const)('does not mistake %s/%s for ordinary membership or loop on browser focus', async (status, code) => {
  const fetcher = mount(status, code)
  await screen.findAllByRole('alert')
  expect(screen.getByTestId('path')).toHaveTextContent('/entrada')
  await act(async () => { focusManager.setFocused(false); focusManager.setFocused(true); await new Promise(resolve => setTimeout(resolve, 20)) })
  expect(fetcher.mock.calls.filter(([path]) => path === '/platform/session')).toHaveLength(1)
  expect(fetcher.mock.calls.some(([path]) => path === '/api/me/tenants')).toBe(false)
  expect(screen.queryByRole('navigation', { name: 'Navegación de plataforma' })).not.toBeInTheDocument()
})
it('keeps a direct platform URL denied for an ordinary session without exposing the console', async () => {
  const fetcher = mount(403, 'access.platform_denied', '/plataforma')
  await screen.findByText(/Esta cuenta no tiene autoridad de plataforma/)
  await waitFor(() => expect(fetcher.mock.calls.filter(([path]) => path === '/platform/session')).toHaveLength(1))
  expect(screen.queryByRole('heading', { name: 'Consola autorizada de prueba' })).not.toBeInTheDocument()
})
it('expires a 401 session instead of treating it as an ordinary Owner or looping through entry', async () => {
  const fetcher = mount(401, 'auth.session_required')
  await screen.findByText(/Tu sesión terminó. Ingresa de nuevo/)
  expect(screen.getByTestId('path')).toHaveTextContent('/')
  expect(screen.getByRole('button', { name: 'Continuar con Google' })).toBeEnabled()
  expect(fetcher.mock.calls.filter(([path]) => path === '/platform/session')).toHaveLength(1)
  expect(fetcher.mock.calls.some(([path]) => String(path).startsWith('/api/me/tenants'))).toBe(false)
})
