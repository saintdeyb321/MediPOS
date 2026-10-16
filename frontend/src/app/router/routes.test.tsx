import { describe, expect, it, vi } from 'vitest'
import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { AppRoutes } from './routes'
import { RuntimeProvider } from '../providers/runtime'
import type { Runtime } from '../../features/access'
import { createPreviewService } from '../../development/fixtures'
import type { Scenario } from '../../types/development'
import { HttpClient } from '../../shared/api/http'
import { ApiError } from '../../shared/api/http'
import { createMediPosService } from '../../features/access'

const tenantId = '019a0162-0000-7000-8000-000000000002'
const branchId = '019a0162-0000-7000-8000-000000000005'
const workspace = `/boticas/${tenantId}/sucursales/${branchId}`
function mount(scenario: Scenario = 'owner', path = '/', overrides?: Partial<Runtime>) {
  const runtime: Runtime = { service: createPreviewService(scenario), preview: true, initiallyExpired: scenario === 'expired', ...overrides }
  render(<RuntimeProvider runtime={runtime}><MemoryRouter initialEntries={[path]}><AppRoutes /></MemoryRouter></RuntimeProvider>)
  return runtime.service
}
describe('F1 navigation and access boundaries', () => {
  // Several async screens plus native user interactions; retain a bounded budget under build load.
  it('walks botica → branch → dashboard → POS shell → logout without creating a sale', async () => {
    const user = userEvent.setup(); mount()
    await user.click(await screen.findByRole('button', { name: /seleccionar botica/i }))
    await user.click(await screen.findByRole('link', { name: /sucursal principal/i }))
    expect(await screen.findByRole('heading', { name: 'Todo listo para empezar.' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Reportes' })).toBeInTheDocument()
    await user.click(screen.getByRole('link', { name: 'Abrir punto de venta' }))
    expect(await screen.findByRole('heading', { name: 'Punto de venta', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /continuar al cobro/i })).toBeDisabled()
    expect(screen.getByRole('searchbox', { name: /buscar un producto/i })).toBeDisabled()
    expect(screen.getByRole('searchbox')).toHaveAccessibleDescription(/conexión real con MediPOS/)
    expect(screen.getByRole('button', { name: /continuar al cobro/i })).toHaveAccessibleDescription(/guarda todas las líneas del borrador con tu caja abierta/)
    await user.click(screen.getByText('Propietario', { selector: '.account-label' }))
    await user.click(screen.getByRole('button', { name: 'Cerrar sesión' }))
    expect(await screen.findByRole('heading', { name: 'Un buen día empieza aquí.' })).toBeInTheDocument()
  }, 15_000)
  it('does not use the descriptive Owner role in the directory to grant cashier menus', async () => {
    const service = createPreviewService('cashier'); const original = service.tenants
    service.tenants = async (...args) => { const page = await original(...args); return { ...page, items: page.items.map(t => ({ ...t, role: 'owner' as const })) } }
    mount('cashier', workspace, { service })
    expect(await screen.findByRole('heading', { name: 'Todo listo para empezar.' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Reportes' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Empleados' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Inventario' })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: /^punto de venta/i })).toBeInTheDocument()
  })
  it('blocks a manually entered managerial page for a cashier', async () => {
    mount('cashier', workspace + '/reportes')
    expect(await screen.findByRole('heading', { name: 'Esta sección requiere otro acceso' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Reportes' })).not.toBeInTheDocument()
  })
  it('rechecks access after navigation and removes an Owner menu after a role change', async () => {
    const owner = createPreviewService('owner'); const cashier = createPreviewService('cashier')
    const access = vi.spyOn(owner, 'access'); mount('owner', workspace, { service: owner })
    await screen.findByRole('heading', { name: 'Todo listo para empezar.' })
    access.mockImplementation((...args) => cashier.access(...args))
    await userEvent.click(screen.getByRole('link', { name: 'Reportes' }))
    expect(await screen.findByRole('heading', { name: 'Esta sección requiere otro acceso' })).toBeInTheDocument()
    expect(access).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('link', { name: 'Reportes' })).not.toBeInTheDocument()
  })
  it('rejects malformed scope identifiers before any branch or access request', async () => {
    const service = createPreviewService('owner'); const access = vi.spyOn(service, 'access'); const branches = vi.spyOn(service, 'branches')
    mount('owner', '/boticas/not-a-uuid/sucursales/not-a-uuid', { service })
    expect(await screen.findByRole('heading', { name: 'Dirección no válida' })).toBeInTheDocument()
    expect(access).not.toHaveBeenCalled(); expect(branches).not.toHaveBeenCalled()
  })
  it('offers a path back to login when a protected deep link cannot check its session', async () => {
    const service = createPreviewService('owner')
    vi.spyOn(service, 'session').mockRejectedValue(new ApiError(0, 'client.network_error'))
    mount('owner', workspace, { service })
    expect(await screen.findByRole('link', { name: 'Volver al ingreso' })).toHaveAttribute('href', '/')
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })
  it('cancels pending Google navigation when the login screen unmounts', async () => {
    const service = createPreviewService('owner'); service.session = async () => null
    let loginSignal: AbortSignal | undefined
    service.login = signal => new Promise<void>((_, reject) => {
      loginSignal = signal
      signal?.addEventListener('abort', () => reject(signal.reason), { once: true })
    })
    const view = render(<RuntimeProvider runtime={{ service, preview: false }}><MemoryRouter><AppRoutes /></MemoryRouter></RuntimeProvider>)
    const login = await screen.findByRole('button', { name: 'Continuar con Google' })
    await userEvent.click(login)
    await waitFor(() => expect(loginSignal).toBeDefined())
    view.unmount(); expect(loginSignal?.aborted).toBe(true)
  })
  it('shows inventory and purchasing for Pharmacist without owner reports', async () => {
    mount('pharmacist', workspace)
    await screen.findByRole('heading', { name: 'Todo listo para empezar.' })
    expect(screen.getByRole('link', { name: 'Inventario' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Compras' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Reportes' })).not.toBeInTheDocument()
  })
  it.each([['scheduleDenied', /fuera de tu horario/i], ['licenseDenied', /licencia/i]] as const)('blocks operational content for %s', async (scenario, message) => {
    mount(scenario, workspace)
    expect(await screen.findByRole('heading', { name: 'Tu acceso está restringido' })).toBeInTheDocument()
    expect(screen.getByText(message)).toBeInTheDocument(); expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })
  it('rejects an unassigned branch even when it is syntactically a valid UUID', async () => {
    mount('cashier', `/boticas/${tenantId}/sucursales/019a0162-0000-7000-8000-000000000006`)
    expect(await screen.findByText(/No tienes asignada esta sucursal/i)).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /punto de venta/i })).not.toBeInTheDocument()
  })
  it('handles empty memberships, empty branches and a recoverable list error', async () => {
    mount('noTenants', '/seleccionar-botica')
    expect(await screen.findByRole('heading', { name: 'Aún no tienes boticas asignadas' })).toBeInTheDocument()
  })
  it('shows an empty authorized branch list without inventing a branch', async () => {
    mount('noBranches', `/boticas/${tenantId}/sucursales`)
    expect(await screen.findByRole('heading', { name: 'No hay sucursales disponibles' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /sucursal principal/i })).not.toBeInTheDocument()
  })
  it('offers a real retry after the tenant request fails', async () => {
    const service = createPreviewService('tenantError'); const tenants = vi.spyOn(service, 'tenants')
    mount('tenantError', '/seleccionar-botica', { service })
    const retry = await screen.findByRole('button', { name: 'Volver a intentar' })
    await userEvent.click(retry)
    await waitFor(() => expect(tenants).toHaveBeenCalledTimes(2))
  })
  it('announces loading before reporting an empty tenant directory', async () => {
    const service = createPreviewService('noTenants')
    let complete!: (value: Awaited<ReturnType<typeof service.tenants>>) => void
    service.tenants = () => new Promise(resolve => { complete = resolve })
    mount('noTenants', '/seleccionar-botica', { service })
    expect(await screen.findByRole('status')).toHaveTextContent(/Verificando tu sesión|Buscando tus boticas/)
    await screen.findByText('Buscando tus boticas…')
    expect(screen.queryByText('Aún no tienes boticas asignadas')).not.toBeInTheDocument()
    await act(async () => complete({ items: [], nextAfterTenantId: null }))
    expect(await screen.findByRole('heading', { name: 'Aún no tienes boticas asignadas' })).toBeInTheDocument()
  })
  it('selects a tenant with the keyboard and announces the next step', async () => {
    mount()
    const selection = await screen.findByRole('button', { name: /seleccionar botica/i })
    const user = userEvent.setup()
    for (let i = 0; i < 6 && document.activeElement !== selection; i++) await user.tab()
    expect(selection).toHaveFocus()
    await user.keyboard('{Enter}')
    expect(await screen.findByRole('heading', { name: 'Cada sucursal, bajo control.' })).toBeInTheDocument()
    expect(screen.getByText('Sucursal', { selector: '[aria-current="step"]' })).toBeInTheDocument()
  })
  it('describes future navigation as unavailable for operations', async () => {
    mount('owner', workspace)
    const reports = await screen.findByRole('link', { name: 'Reportes' })
    expect(reports).toHaveAccessibleDescription('Próximamente: esta sección aún no permite operaciones.')
  })
  it('redirects an unauthenticated deep link to login and explains session expiration', async () => {
    mount('expired', workspace)
    expect(await screen.findByText(/Tu sesión terminó. Ingresa de nuevo/i)).toBeInTheDocument()
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })
  it('clears protected navigation when a real HTTP 401 expires the current session', async () => {
    let rejected = false
    const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => {
      const ok = path === '/auth/me' && !rejected
      if (path !== '/auth/me') rejected = true
      return new Response(JSON.stringify(ok ? { userId: '019a0162-0000-7000-8000-000000000001' } : { code: 'auth.session_required' }), { status: ok ? 200 : 401, headers: { 'Content-Type': 'application/json' } })
    })
    const http = new HttpClient(fetcher)
    mount('owner', '/', { service: createMediPosService(http), http, preview: false })
    expect(await screen.findByText(/Tu sesión terminó. Ingresa de nuevo/i)).toBeInTheDocument()
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
    expect(fetcher.mock.calls.some(([path]) => typeof path === 'string' && path.startsWith('/api/me/tenants'))).toBe(true)
    expect(screen.getByRole('button', { name: 'Continuar con Google' })).toBeEnabled()
  })
})
