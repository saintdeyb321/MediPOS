import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { RuntimeProvider } from '../../app/providers/runtime'
import { createMediPosService } from '../access'
import { HttpClient } from '../../shared/api/http'
import { PlatformLayout } from '../../app/layouts/PlatformLayout'
import { PlatformPage } from './PlatformPage'

// Synthetic transport only: these tests do not certify Google or PostgreSQL.
const id = (n: number) => `019a0162-0000-7000-8000-${String(n).padStart(12, '0')}`
const time = '2026-10-17T12:00:00Z'
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
type Invitation = { invitationId: string; email: string; status: 'pending' | 'claimed' | 'revoked'; createdAt: string; userId: string | null }
function fixture() {
  const state = { admission: 200, lost: false, invisible: false, duplicateCategory: false, reject: 0, listFailure: 0, detailFailure: 0, receipt: false, owner: null as string | null, invitation: null as Invitation | null, status: 'trial', categories: [] as { id: string; name: string; isActive: boolean; createdAt: string }[] }
  const license = () => ({ tenantId: id(2), licenseId: id(3), status: state.status, startsAt: time, expiresAt: '2026-11-16T12:00:00Z', maxBranches: 2 })
  const tenant = () => ({ tenantId: id(2), tradingName: 'Synthetic QA botica', createdAt: time, administration: { licenseStatus: state.status, expiresAt: license().expiresAt, maxBranches: 2, ownerStatus: state.invitation?.status ?? (state.owner ? 'active' : 'unassigned'), ownerEmail: state.invitation?.email ?? null, ownerName: null } })
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (path, init) => {
    const p = String(path).split('?')[0]; const method = init?.method ?? 'GET'
    if (p === '/auth/me') return json({ userId: id(1) })
    if (p === '/auth/csrf') return json({ requestToken: 'synthetic-csrf' })
    if (p === '/platform/session') return state.admission === 200 ? json({ allowed: true }) : json({ code: 'access.platform_denied' }, state.admission)
    if (method === 'POST' && state.reject) return json({ code: 'unit.rejected' }, state.reject)
    if (p === '/platform/tenants/with-owner' && method === 'POST') {
      const input = JSON.parse(String(init?.body)) as { ownerEmail: string }
      state.receipt = true; state.invitation = { invitationId: id(6), email: input.ownerEmail, status: 'pending', createdAt: time, userId: null }
      if (state.lost) throw new TypeError('Synthetic lost response'); return json(license(), 201)
    }
    if (p?.startsWith('/platform/tenant-requests/')) return state.receipt && !state.invisible ? json(license()) : json({ code: 'platform.request_not_found' }, 404)
    if (p === '/platform/catalog/categories' && method === 'POST') {
      const name = (JSON.parse(String(init?.body)) as { name: string }).name
      const row = { id: id(9), name, isActive: true, createdAt: time }; state.categories.push(row)
      if (state.duplicateCategory) state.categories.push({ ...row, id: id(10) })
      if (state.lost) throw new TypeError('Synthetic lost response'); return json(row, 201)
    }
    if (p === '/platform/catalog/categories') return json(state.invisible ? [] : state.categories)
    if (p === '/platform/tenants') return state.listFailure ? json({ code: 'unit.read_failed' }, state.listFailure) : json({ items: [tenant()], nextAfterTenantId: null })
    if (p?.endsWith('/details')) return state.detailFailure ? json({ code: 'access.platform_denied' }, state.detailFailure) : json({ tenant: tenant(), license: license(), initialOwnerUserId: state.owner })
    if (p?.endsWith('/initial-owner')) return json({ invitation: state.invitation })
    if (p?.endsWith('/initial-owner/change') || p?.endsWith('/initial-owner/revoke')) {
      const body = JSON.parse(String(init?.body)) as { expectedInvitationId: string; email?: string }
      expect(body.expectedInvitationId).toBe(state.invitation?.invitationId)
      state.invitation = { invitationId: body.email ? id(7) : id(6), email: body.email ?? state.invitation!.email, status: body.email ? 'pending' : 'revoked', createdAt: time, userId: null }
      if (state.lost) throw new TypeError('Synthetic lost response'); return json(state.invitation)
    }
    if (method === 'POST' && /\/(suspend|renew|reactivate)$/.test(p!)) {
      state.status = p?.endsWith('/suspend') ? 'suspended' : 'active'
      if (state.lost) throw new TypeError('Synthetic lost response'); return json(license())
    }
    throw new Error('Unexpected or retired UI route: ' + p)
  })
  return { state, fetcher, writes: () => fetcher.mock.calls.filter(([, init]) => init?.method === 'POST') }
}
function Location() { return <output data-testid="location">{useLocation().search}</output> }
function mount(fake: ReturnType<typeof fixture>, query = '') {
  const http = new HttpClient(fake.fetcher)
  return render(<RuntimeProvider runtime={{ service: createMediPosService(http), http, preview: false }}><MemoryRouter initialEntries={['/plataforma' + query]}><Routes><Route path="/plataforma" element={<PlatformLayout />}><Route index element={<PlatformPage />} /></Route></Routes><Location /></MemoryRouter></RuntimeProvider>)
}
async function create() {
  fireEvent.click(await screen.findByRole('button', { name: 'Nueva botica' }))
  const dialog = screen.getByRole('dialog', { name: 'Nueva botica' }); const form = within(dialog)
  fireEvent.change(form.getByLabelText('Nombre comercial'), { target: { value: 'Synthetic new botica' } })
  fireEvent.click(form.getByRole('button', { name: 'Continuar' }))
  fireEvent.change(form.getByLabelText('Gmail del propietario'), { target: { value: 'future.owner@example.invalid' } })
  fireEvent.click(form.getByRole('button', { name: 'Continuar' }))
  fireEvent.click(form.getByRole('checkbox', { name: 'Confirmo el negocio, Gmail y licencia.' }))
  fireEvent.click(form.getByRole('button', { name: 'Crear botica y asignación pendiente' }))
  return dialog
}
const pending = (): Invitation => ({ invitationId: id(6), email: 'future.owner@example.invalid', status: 'pending', createdAt: time, userId: null })

it('denies an ordinary Google session before reading global data and renders one logout', async () => {
  const fake = fixture(); fake.state.admission = 403; mount(fake)
  await screen.findByText(/Esta cuenta no tiene autoridad/)
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).startsWith('/platform/tenants'))).toBe(false)
  expect(screen.queryByRole('button', { name: 'Nueva botica' })).not.toBeInTheDocument()
  expect(screen.getAllByRole('button', { name: 'Cerrar sesión' })).toHaveLength(1)
})
it('does not let a 503 list hide a concurrent 403 authority revocation', async () => {
  const fake = fixture(); fake.state.listFailure = 503; fake.state.detailFailure = 403; mount(fake, '?tenant=' + id(2))
  await screen.findByText(/La autoridad de plataforma ya no está disponible/)
  expect(screen.queryByRole('button', { name: 'Nueva botica' })).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
it('creates with Gmail/CSRF/idempotency, closes the confirmed wizard and clears dirty', async () => {
  const fake = fixture(); mount(fake); await create()
  await screen.findByRole('dialog', { name: 'Ficha de botica' })
  expect(screen.queryByRole('dialog', { name: 'Nueva botica' })).not.toBeInTheDocument()
  expect(fake.writes()).toHaveLength(1)
  const [path, init] = fake.writes()[0]!; const headers = new Headers(init!.headers)
  expect(path).toBe('/platform/tenants/with-owner'); expect(headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/); expect(headers.get('X-CSRF-TOKEN')).toBe('synthetic-csrf')
  const body = JSON.parse(String(init!.body)) as { ownerEmail: string; status: string; startsAt: string; expiresAt: string }
  expect(body.ownerEmail).toBe('future.owner@example.invalid'); expect(body.status).toBe('trial'); expect(Date.parse(body.expiresAt) - Date.parse(body.startsAt)).toBe(30 * 86400000)
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('creationKey'))
  fireEvent.click(screen.getByRole('button', { name: 'Cerrar Ficha de botica' }))
  fireEvent.click(screen.getByRole('button', { name: 'Nueva botica' }))
  expect(screen.getByLabelText('Nombre comercial')).toHaveValue('')
  fireEvent.click(screen.getByRole('button', { name: 'Cerrar Nueva botica' }))
  expect(screen.queryByText('¿Salir del formulario?')).not.toBeInTheDocument()
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('bootstrap-owner') || String(path).includes('/identities/'))).toBe(false)
})
it('retains the original creation and form after a lost response/404, then reconciles with GET only', async () => {
  const fake = fixture(); fake.state.lost = true; fake.state.invisible = true; mount(fake); const dialog = await create()
  const recovery = await within(dialog).findByRole('button', { name: 'Consultar constancia original sin repetir POST' })
  const address = screen.getByTestId('location').textContent
  expect(within(dialog).getByLabelText('Nombre comercial')).toHaveValue('Synthetic new botica')
  fireEvent.click(recovery)
  await waitFor(() => expect(fake.fetcher.mock.calls.some(([path]) => String(path).startsWith('/platform/tenant-requests/'))).toBe(true))
  await waitFor(() => expect(recovery).toBeEnabled())
  expect(screen.getByTestId('location').textContent).toBe(address); expect(fake.writes()).toHaveLength(1)
  fireEvent.click(within(dialog).getByRole('button', { name: 'Volver a la página' }))
  expect(screen.queryByText('¿Salir del formulario?')).not.toBeInTheDocument()
  fake.state.invisible = false
  fireEvent.click(screen.getByRole('button', { name: 'Consultar constancia original sin repetir POST' }))
  await screen.findByRole('dialog', { name: 'Ficha de botica' }); expect(fake.writes()).toHaveLength(1)
})
it('recovers the original creation after reload without any POST', async () => {
  const fake = fixture(); fake.state.receipt = true; fake.state.invitation = pending(); mount(fake, '?creationKey=' + id(8) + '&platformPending=create')
  fireEvent.click(await screen.findByRole('button', { name: 'Consultar constancia original sin repetir POST' }))
  await screen.findByRole('dialog', { name: 'Ficha de botica' }); expect(fake.writes()).toHaveLength(0)
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('creationKey'))
})
it.each([400, 403, 409, 429, 503])('does not retry a rejected or uncertain creation with HTTP %i', async status => {
  const fake = fixture(); fake.state.reject = status; mount(fake); await create()
  await waitFor(() => expect(fake.writes()).toHaveLength(1))
  await screen.findAllByRole('alert')
  expect(fake.writes()).toHaveLength(1)
  if ([409, 503].includes(status)) expect(screen.getByTestId('location')).toHaveTextContent('creationKey')
  else await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('creationKey'))
})
it.each(['change', 'revoke'])('uses the original invitation identity for Owner %s and closes after a confirmed read', async action => {
  const fake = fixture(); fake.state.invitation = pending(); mount(fake, '?tenant=' + id(2))
  fireEvent.click(await screen.findByRole('button', { name: 'Corregir o revocar Gmail pendiente' }))
  if (action === 'change') fireEvent.change(screen.getByLabelText('Gmail del propietario pendiente'), { target: { value: 'corrected@example.invalid' } })
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirmo el cambio o revocación del destinatario pendiente.' }))
  fireEvent.click(screen.getByRole('button', { name: action === 'change' ? 'Guardar Gmail pendiente' : 'Revocar asignación pendiente' }))
  await screen.findByRole('dialog', { name: 'Ficha de botica' }); expect(fake.writes()).toHaveLength(1)
  expect(JSON.parse(String(fake.writes()[0]![1]!.body))).toMatchObject({ expectedInvitationId: id(6) })
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('ownerExpected'))
})
it('shows an already claimed Owner without permitting an initial reassignment', async () => {
  const fake = fixture(); fake.state.owner = id(4); fake.state.invitation = { ...pending(), status: 'claimed', userId: id(4) }; mount(fake, '?tenant=' + id(2))
  await screen.findByText(/Propietario inicial registrado/)
  expect(screen.queryByRole('button', { name: 'Corregir o revocar Gmail pendiente' })).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
it('reconciles an uncertain suspension with fresh reads before clearing its marker, without repeating POST', async () => {
  const fake = fixture(); fake.state.lost = true; mount(fake, '?tenant=' + id(2))
  fireEvent.click(await screen.findByRole('button', { name: 'Gestionar licencia' }))
  fireEvent.change(screen.getByRole('combobox', { name: 'Operación de licencia' }), { target: { value: 'suspend' } })
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirmo el cambio de la licencia seleccionada.' })); fireEvent.click(screen.getByRole('button', { name: 'Aplicar cambio de licencia' }))
  const dialog = screen.getByRole('dialog', { name: 'Gestionar licencia' })
  const read = await within(dialog).findByRole('button', { name: 'Reconsultar licencia y propietario' })
  await waitFor(() => expect(read).toBeEnabled()); fireEvent.click(read)
  const confirm = within(dialog).getByRole('checkbox', { name: /Verifiqué el resultado original/ })
  await waitFor(() => expect(confirm).toBeEnabled()); fireEvent.click(confirm)
  fireEvent.click(within(dialog).getByRole('button', { name: 'Continuar tras conciliación' }))
  await screen.findByRole('dialog', { name: 'Ficha de botica' }); expect(fake.writes()).toHaveLength(1)
  expect(screen.getByTestId('location')).not.toHaveTextContent('platformPending')
})
it.each(['renew', 'reactivate'] as const)('closes a confirmed %s and keeps the server license identity', async action => {
  const fake = fixture(); if (action === 'reactivate') fake.state.status = 'suspended'; mount(fake, '?tenant=' + id(2))
  fireEvent.click(await screen.findByRole('button', { name: 'Gestionar licencia' }))
  if (action === 'renew') fireEvent.change(screen.getByLabelText('Nuevo vencimiento (UTC)'), { target: { value: '2026-12-17T12:00' } })
  else fireEvent.change(screen.getByRole('combobox', { name: 'Operación de licencia' }), { target: { value: action } })
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirmo el cambio de la licencia seleccionada.' }))
  fireEvent.click(screen.getByRole('button', { name: 'Aplicar cambio de licencia' }))
  await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Gestionar licencia' })).not.toBeInTheDocument())
  expect(fake.writes()).toHaveLength(1)
  expect(fake.writes()[0]![0]).toBe(`/platform/tenants/${id(2)}/licenses/${id(3)}/${action}`)
  expect(JSON.parse(String(fake.writes()[0]![1]!.body))).toEqual(action === 'renew' ? { expiresAt: '2026-12-17T12:00:00Z' } : { status: 'active' })
  expect(screen.queryByText('¿Salir del formulario?')).not.toBeInTheDocument()
})
it('requires a category read, prevents duplicate creation and closes on confirmed persistence', async () => {
  const fake = fixture(); mount(fake, '?area=catalog')
  await screen.findByRole('heading', { name: 'Catálogo global' })
  expect(screen.getByRole('button', { name: 'Nueva categoría' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Consultar categorías globales' })); await screen.findByText('No hay categorías activas.')
  fireEvent.click(screen.getByRole('button', { name: 'Nueva categoría' })); fireEvent.change(screen.getByLabelText('Nombre de la categoría global'), { target: { value: 'Medicamentos' } })
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirmo crear esta referencia global, sin productos.' }))
  fireEvent.click(screen.getByRole('button', { name: 'Crear categoría global una vez' }))
  await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Crear categoría global' })).not.toBeInTheDocument())
  expect(fake.writes()).toHaveLength(1); expect(new Headers(fake.writes()[0]![1]!.headers).has('Idempotency-Key')).toBe(false)
})
it('keeps a lost category intent after an empty read and blocks ambiguous duplicate names', async () => {
  const fake = fixture(); mount(fake, '?area=catalog&categoryPending=Medicamentos')
  const read = await screen.findByRole('button', { name: 'Reconsultar categorías' }); fireEvent.click(read)
  await screen.findByText('No hay categorías activas.')
  expect(screen.getByRole('button', { name: 'Continuar tras conciliar categoría' })).toBeDisabled()
  fake.state.categories.push({ id: id(9), name: 'Medicamentos', isActive: true, createdAt: time }, { id: id(10), name: 'medicamentos', isActive: true, createdAt: time })
  await waitFor(() => expect(read).toBeEnabled()); fireEvent.click(read)
  await screen.findAllByText(/Hay categorías con el mismo nombre/)
  expect(screen.getByRole('button', { name: 'Continuar tras conciliar categoría' })).toBeDisabled(); expect(fake.writes()).toHaveLength(0)
})
it('keeps a mismatched post-create category response pending instead of reporting success', async () => {
  const fake = fixture(); fake.state.duplicateCategory = true; mount(fake, '?area=catalog')
  fireEvent.click(await screen.findByRole('button', { name: 'Consultar categorías globales' })); await screen.findByText('No hay categorías activas.')
  fireEvent.click(screen.getByRole('button', { name: 'Nueva categoría' })); fireEvent.change(screen.getByLabelText('Nombre de la categoría global'), { target: { value: 'Medicamentos' } })
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirmo crear esta referencia global, sin productos.' })); fireEvent.click(screen.getByRole('button', { name: 'Crear categoría global una vez' }))
  await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('categoryPending'))
  const dialog = screen.getByRole('dialog', { name: 'Crear categoría global' })
  await within(dialog).findByText(/Hay categorías con el mismo nombre/)
  expect(fake.writes()).toHaveLength(1); expect(screen.queryByText(/Categoría creada mediante el contrato oficial/)).not.toBeInTheDocument()
})
