import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { MemoryRouter, useLocation } from 'react-router'
import { RuntimeProvider } from '../../app/providers/runtime'
import { createMediPosService } from '../access'
import { HttpClient } from '../../shared/api/http'
import { PlatformPage } from './PlatformPage'

// Synthetic transport for UI regression only. Does not certify Google, PostgreSQL or browser acceptance.
const id = (n: number) => `019a0162-0000-7000-8000-${String(n).padStart(12, '0')}`
const time = '2026-10-17T12:00:00Z'
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
function fixture() {
  const state = { allowed: true, lost: false, invisible: false, reject: 0, listFailure: 0, detailFailure: 0, receipt: false, owner: null as string | null, status: 'trial', categories: [] as { id: string; name: string; isActive: boolean; createdAt: string }[] }
  const license = () => ({ tenantId: id(2), licenseId: id(3), status: state.status, startsAt: time, expiresAt: '2026-11-16T12:00:00Z', maxBranches: 2 })
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (path, init) => {
    const p = String(path).split('?')[0]; const method = init?.method ?? 'GET'
    if (p === '/auth/me') return json({ userId: id(1) })
    if (p === '/auth/csrf') return json({ requestToken: 'synthetic-csrf' })
    if (p === '/platform/session') return state.allowed ? json({ allowed: true }) : json({ code: 'access.platform_denied' }, 403)
    if (method === 'POST' && state.reject) return json({ code: 'unit.rejected' }, state.reject)
    if (p === '/platform/catalog/categories' && method === 'POST') {
      const name = (JSON.parse(String(init?.body)) as { name: string }).name
      const created = { id: id(9), name, isActive: true, createdAt: time }; state.categories.push(created)
      if (state.lost) throw new TypeError('synthetic lost response'); return json(created, 201)
    }
    if (p === '/platform/catalog/categories') return json(state.invisible ? [] : state.categories)
    if (p === '/platform/tenants' && method === 'POST') { state.receipt = true; if (state.lost) throw new TypeError('synthetic lost response'); return json(license(), 201) }
    if (p === '/platform/tenants') return state.listFailure ? json({ code: 'unit.read_failed' }, state.listFailure) : json({ items: [{ tenantId: id(2), tradingName: 'Synthetic QA botica', createdAt: time }], nextAfterTenantId: null })
    if (p?.startsWith('/platform/tenant-requests/')) return state.receipt && !state.invisible ? json(license()) : json({ code: 'platform.request_not_found' }, 404)
    if (p === `/platform/identities/${id(4)}`) return json({ userId: id(4), displayName: 'Synthetic registered Owner', email: 'qa-owner@example.invalid' })
    if (p?.endsWith('/bootstrap-owner')) { state.owner = id(4); if (state.lost) throw new TypeError('synthetic lost response'); return json({ tenantId: id(2), membershipId: id(5), userId: id(4), role: 'owner', isActive: true, createdAt: time }, 201) }
    if (method === 'POST') { if (p?.endsWith('/suspend')) state.status = 'suspended'; if (p?.endsWith('/reactivate')) state.status = 'active'; if (state.lost) throw new TypeError('synthetic lost response'); return json(license()) }
    if (p === `/platform/tenants/${id(2)}/details`) return state.detailFailure ? json({ code: 'access.platform_denied' }, state.detailFailure) : json({ tenant: { tenantId: id(2), tradingName: 'Synthetic QA botica', createdAt: time }, license: license(), initialOwnerUserId: state.owner })
    return json({ code: 'unit.not_found' }, 404)
  })
  return { state, fetcher, writes: () => fetcher.mock.calls.filter(([, init]) => init?.method === 'POST') }
}
function Location() { return <output data-testid="location">{useLocation().search}</output> }
function mount(fake: ReturnType<typeof fixture>, query = '') {
  const http = new HttpClient(fake.fetcher)
  return render(<RuntimeProvider runtime={{ service: createMediPosService(http), http, preview: false }}><MemoryRouter initialEntries={['/plataforma' + query]}><PlatformPage /><Location /></MemoryRouter></RuntimeProvider>)
}
async function create() {
  await screen.findByRole('heading', { name: 'Administración de plataforma' })
  fireEvent.change(screen.getByRole('textbox', { name: 'Nombre comercial' }), { target: { value: 'Synthetic QA new botica' } })
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirmo crear esta botica y su licencia.' }))
  fireEvent.click(screen.getByRole('button', { name: 'Crear botica de prueba' }))
}
it('denies ordinary sessions before reading any global directory', async () => {
  const fake = fixture(); fake.state.allowed = false; mount(fake)
  await screen.findByText(/Ser Owner no concede este acceso/)
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).startsWith('/platform/tenants'))).toBe(false)
  expect(screen.queryByRole('button', { name: 'Crear botica de prueba' })).not.toBeInTheDocument()
})
it('blocks the whole console when a concurrent 503 must not mask a 403 revocation', async () => {
  const fake = fixture(); fake.state.listFailure = 503; fake.state.detailFailure = 403; mount(fake, '?tenant=' + id(2))
  await screen.findByText(/La autoridad de plataforma ya no está disponible/)
  expect(screen.queryByRole('button', { name: 'Crear botica de prueba' })).not.toBeInTheDocument()
  expect(fake.writes()).toHaveLength(0)
})
it('creates one trial with an exact idempotency header and reads its receipt before selecting it', async () => {
  const fake = fixture(); mount(fake); await create()
  await screen.findByRole('heading', { name: 'Synthetic QA botica · licencia' })
  expect(fake.writes()).toHaveLength(1)
  const request = fake.writes()[0]![1]!; const headers = new Headers(request.headers)
  expect(headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/); expect(headers.get('X-CSRF-TOKEN')).toBe('synthetic-csrf')
  const body = JSON.parse(String(request.body)) as { status: string; maxBranches: number; startsAt: string; expiresAt: string }
  expect(body.status).toBe('trial'); expect(body.maxBranches).toBe(2); expect(Date.parse(body.expiresAt) - Date.parse(body.startsAt)).toBe(30 * 86400000)
  expect(screen.getByRole('button', { name: 'Crear botica de prueba' })).toBeDisabled() // new empty form, not a stuck busy operation
  fireEvent.change(screen.getByRole('textbox', { name: 'Nombre comercial' }), { target: { value: 'Next explicit input' } })
  expect(screen.getByRole('textbox', { name: 'Nombre comercial' })).toBeEnabled()
})
it('keeps the original key after a lost POST and a 404 read, then reconciles with GET only', async () => {
  const fake = fixture(); fake.state.lost = true; fake.state.invisible = true; mount(fake); await create()
  await screen.findByRole('button', { name: 'Consultar constancia original sin repetir POST' })
  const before = screen.getByTestId('location').textContent
  fireEvent.click(screen.getByRole('button', { name: 'Consultar constancia original sin repetir POST' }))
  await waitFor(() => expect(fake.fetcher.mock.calls.some(([path]) => String(path).startsWith('/platform/tenant-requests/'))).toBe(true))
  expect(screen.getByTestId('location').textContent).toBe(before); expect(fake.writes()).toHaveLength(1)
  fake.state.invisible = false; await waitFor(() => expect(screen.getByRole('button', { name: 'Consultar constancia original sin repetir POST' })).toBeEnabled())
  fireEvent.click(screen.getByRole('button', { name: 'Consultar constancia original sin repetir POST' }))
  await screen.findByRole('heading', { name: 'Synthetic QA botica · licencia' }); expect(fake.writes()).toHaveLength(1)
})
it('reloads an uncertain creation into reconciliation without dispatching a mutation', async () => {
  const fake = fixture(); fake.state.receipt = true; mount(fake, '?creationKey=' + id(8) + '&platformPending=create')
  await screen.findByRole('button', { name: 'Consultar constancia original sin repetir POST' }); expect(fake.writes()).toHaveLength(0)
  expect(screen.getByRole('button', { name: 'Crear botica de prueba' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Consultar constancia original sin repetir POST' }))
  await screen.findByRole('heading', { name: 'Synthetic QA botica · licencia' }); expect(fake.writes()).toHaveLength(0)
})
it('requires an exact server-verified Google identity and explicit approval for the first Owner', async () => {
  const fake = fixture(); mount(fake, '?tenant=' + id(2)); await screen.findByRole('heading', { name: 'Primer Owner' })
  expect(screen.getByRole('button', { name: 'Asignar primer Owner' })).toBeDisabled()
  fireEvent.change(screen.getByRole('textbox', { name: 'Identificador interno del futuro Owner' }), { target: { value: id(4) } })
  fireEvent.click(screen.getByRole('button', { name: 'Verificar identidad Google registrada' })); await screen.findByText(/qa-owner@example.invalid/)
  fireEvent.click(screen.getByRole('checkbox', { name: /Confirmo que esta es la cuenta destinataria/ }))
  fireEvent.click(screen.getByRole('button', { name: 'Asignar primer Owner' })); fireEvent.click(screen.getByRole('button', { name: 'Asignar primer Owner' }))
  await screen.findByText(/Existe historial de Owner/); expect(fake.writes()).toHaveLength(1); expect(fake.writes()[0]![1]!.body).toBe(JSON.stringify({ userId: id(4) }))
})
it('requires a fresh read before acknowledging an ambiguous license mutation and never repeats it', async () => {
  const fake = fixture(); fake.state.lost = true; mount(fake, '?tenant=' + id(2)); await screen.findByRole('heading', { name: 'Primer Owner' })
  fireEvent.change(screen.getByRole('combobox', { name: 'Operación de licencia' }), { target: { value: 'suspend' } })
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirmo el cambio de la licencia seleccionada.' })); fireEvent.click(screen.getByRole('button', { name: 'Aplicar cambio de licencia' }))
  await screen.findByText(/Operación pendiente: suspend/); expect(screen.getByRole('checkbox', { name: /Verifiqué el resultado y que no hay/ })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Reconsultar licencia y Owner' })); await screen.findByText('suspended')
  fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué el resultado y que no hay/ })); fireEvent.click(screen.getByRole('button', { name: 'Continuar tras conciliación' }))
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('platformPending')); expect(fake.writes()).toHaveLength(1)
})
it('recovers an uncertain bootstrap after reload by reading the registered Owner without another POST', async () => {
  const fake = fixture(); fake.state.owner = id(4); mount(fake, '?tenant=' + id(2) + '&platformPending=bootstrap')
  await screen.findByText(/Existe historial de Owner/)
  expect(screen.queryByRole('button', { name: 'Asignar primer Owner' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Reconsultar licencia y Owner' }))
  await waitFor(() => expect(screen.getByRole('button', { name: 'Verificar identidad del Owner inicial' })).toBeEnabled())
  fireEvent.click(screen.getByRole('button', { name: 'Verificar identidad del Owner inicial' }))
  await screen.findByText(/Owner inicial registrado: Synthetic registered Owner · qa-owner@example.invalid/)
  expect(fake.writes()).toHaveLength(0)
})
it('reads before creating a category, prevents double submit and does not recreate an existing name', async () => {
  const fake = fixture(); mount(fake); await screen.findByRole('heading', { name: 'Categoría global inicial' })
  expect(screen.getByRole('button', { name: 'Crear categoría global una vez' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Consultar categorías globales' })); await screen.findByText('No hay categorías activas.')
  fireEvent.change(screen.getByRole('textbox', { name: 'Nombre de la categoría global' }), { target: { value: 'Medicamentos' } })
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirmo crear esta referencia global, sin productos.' }))
  fireEvent.click(screen.getByRole('button', { name: 'Crear categoría global una vez' })); fireEvent.click(screen.getByRole('button', { name: 'Crear categoría global una vez' }))
  await screen.findByText(/Categoría creada mediante el contrato oficial/)
  expect(fake.writes()).toHaveLength(1); expect(fake.writes()[0]![1]!.body).toBe('{"name":"Medicamentos"}')
  expect(new Headers(fake.writes()[0]![1]!.headers).get('X-CSRF-TOKEN')).toBe('synthetic-csrf')
  expect(new Headers(fake.writes()[0]![1]!.headers).has('Idempotency-Key')).toBe(false)
  expect(screen.getByRole('button', { name: 'Crear categoría global una vez' })).toBeDisabled()
})
it('keeps an uncertain category intent across reload and never treats an empty read as failure', async () => {
  const fake = fixture(); fake.state.lost = true; mount(fake, '?categoryPending=Medicamentos')
  await screen.findByText(/Alta de categoría pendiente: Medicamentos/); expect(fake.writes()).toHaveLength(0)
  fireEvent.click(screen.getByRole('button', { name: 'Consultar categorías globales' })); await screen.findByText('No hay categorías activas.')
  expect(screen.getByRole('button', { name: 'Continuar tras conciliar categoría' })).toBeDisabled()
  expect(screen.getByTestId('location')).toHaveTextContent('categoryPending=Medicamentos')
  fake.state.categories.push({ id: id(9), name: 'Medicamentos', isActive: true, createdAt: time })
  await waitFor(() => expect(screen.getByRole('button', { name: 'Consultar categorías globales' })).toBeEnabled())
  fireEvent.click(screen.getByRole('button', { name: 'Consultar categorías globales' }))
  await waitFor(() => expect(screen.getByRole('checkbox', { name: /Verifiqué la categoría registrada/ })).toBeEnabled())
  fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué la categoría registrada/ })); fireEvent.click(screen.getByRole('button', { name: 'Continuar tras conciliar categoría' }))
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('categoryPending'))
  expect(fake.writes()).toHaveLength(0)
})
it.each([403, 409, 429, 503])('shows HTTP %i without automatically retrying a platform mutation', async status => {
  const fake = fixture(); fake.state.reject = status; mount(fake); await create(); await screen.findAllByRole('alert')
  expect(fake.writes()).toHaveLength(1)
})
