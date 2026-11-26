import { QueryClient } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { createMemoryRouter, RouterProvider, Outlet, useLocation } from 'react-router'
import { RuntimeProvider } from '../../app/providers/runtime'
import { createMediPosService } from '../access'
import { NavigationSafety } from '../../shared/ui/NavigationSafety'
import { HttpClient } from '../../shared/api/http'
import { TenantOnboardingPage } from './TenantOnboardingPage'
import { BranchSelection } from '../branches/BranchSelection'
import { refocus } from '../../test/refocus'
import { id, legalEntity, managementServer, workspace, json } from '../notifications/management.test.support'
import { installTestStructureStorage } from './recovery.test.support'

installTestStructureStorage()
const root = `/boticas/${workspace.tenantId}`
const path = `/api/tenants/${workspace.tenantId}`
function Address() { return <output data-testid="address">{useLocation().search}</output> }
function fixture() {
  const base = managementServer()
  base.management.legal = []
  base.management.branches = []
  const state = { role: 'owner', allowed: true, branchFailure: 0, lost: '', mismatch: '', accessFailure: 0, actor: workspace.session.userId, beforeBranch: null as Promise<void> | null, staleBranches: null as Promise<Response> | null }
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (route, init) => {
    const u = new URL(String(route), 'https://unit.test')
    if (u.pathname === '/auth/me') return json({ userId: state.actor })
    if (u.pathname === '/api/me/tenants') return json({ items: [{ tenantId: workspace.tenantId, tradingName: 'Synthetic branchless botica', role: state.role }], nextAfterTenantId: null })
    if (u.pathname.endsWith('/access')) {
      const branchId = u.searchParams.get('branchId')
      if (branchId && state.accessFailure) return json({ code: 'unit.read_failed' }, state.accessFailure)
      return json({ isAllowed: state.allowed && state.role === 'owner', code: !state.allowed ? 'access.license_denied' : state.role === 'owner' ? 'access.allowed' : 'access.branch_required',
        context: { ...workspace.access, membershipId: state.actor === workspace.session.userId ? workspace.access.membershipId : id(800), userId: state.actor, role: state.role, branchId } })
    }
    const kind = u.pathname.endsWith('/branches') ? 'branch' : u.pathname.endsWith('/legal-entities') ? 'legal' : ''
    if (init?.method === 'GET' && kind === 'branch' && state.staleBranches) return state.staleBranches
    if (init?.method === 'POST' && kind) {
      if (kind === 'branch') await state.beforeBranch
      if (kind === 'branch' && state.branchFailure) return json({ code: state.branchFailure === 409 ? 'branch.limit_reached' : 'unit.rejected' }, state.branchFailure)
      const response = await base.fetcher(route, init)
      if (state.lost === kind) throw new TypeError('Synthetic lost response')
      if (state.mismatch === kind) {
        const row = await response.json()
        return json({ ...row, tenantId: id(999) }, 201)
      }
      return response
    }
    return base.fetcher(route, init)
  })
  const mount = (search = '', entry = root + '/configuracion') => {
    const http = new HttpClient(fetcher)
    const router = createMemoryRouter([{ element: <NavigationSafety identity={state.actor}><Outlet /><Address /></NavigationSafety>, children: [
      { path: '/boticas/:tenantId/configuracion', element: <TenantOnboardingPage /> },
      { path: '/boticas/:tenantId/sucursales', element: <BranchSelection /> },
      { path: '/boticas/:tenantId/sucursales/:branchId', element: <h1>Inicio de la sede</h1> },
    ] }], { initialEntries: [entry + search] })
    return render(<RuntimeProvider runtime={{ service: createMediPosService(http), http, preview: false }}><RouterProvider router={router} /></RuntimeProvider>)
  }
  const writes = () => fetcher.mock.calls.filter(([, init]) => init?.method === 'POST')
  return { ...base, fetcher, mount, writes, state }
}
async function first(useLegal = true) {
  if (useLegal) {
    fireEvent.change(await screen.findByRole('textbox', { name: 'Razón social' }), { target: { value: 'Synthetic QA entity' } })
    fireEvent.change(screen.getByRole('textbox', { name: 'RUC' }), { target: { value: 'QA-NO-FISCAL' } })
  }
  fireEvent.change(await screen.findByRole('textbox', { name: 'Nombre de la sucursal' }), { target: { value: 'Synthetic QA first branch' } })
  const submit = screen.getByRole('button', { name: 'Crear sucursal y comenzar' })
  await waitFor(() => expect(submit).toBeEnabled())
  fireEvent.click(submit)
  fireEvent.click(submit)
}

it('creates both records in order using the accepted legal id, then verifies entry with no success checkbox', async () => {
  const fake = fixture(); fake.mount()
  await screen.findByRole('heading', { name: 'Configura tu botica' })
  expect(await screen.findByText('Synthetic branchless botica')).toBeInTheDocument()
  expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1)
  await first()
  await screen.findByRole('heading', { name: 'Inicio de la sede' })
  expect(fake.writes().map(([route]) => route)).toEqual([path + '/legal-entities', path + '/branches'])
  expect(JSON.parse(String(fake.writes()[1]![1]?.body))).toEqual({ legalEntityId: fake.management.legal[0]?.id, name: 'Synthetic QA first branch' })
  expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
  expect(fake.fetcher.mock.calls.filter(([route]) => String(route).includes('/access?branchId='))).toHaveLength(1)
  expect(fake.fetcher.mock.calls.filter(([route]) => route === path + '/access')).toHaveLength(3)
  expect(fake.fetcher.mock.calls.filter(([route]) => route === '/auth/csrf')).toHaveLength(2)
  expect(fake.fetcher.mock.calls.filter(([route]) => String(route).includes('/legal-entities?'))).toHaveLength(2)
  expect(fake.fetcher.mock.calls.filter(([route]) => String(route).includes('/branches?'))).toHaveLength(2)
})
it('preselects a unique authorized legal entity and uses one branch POST', async () => {
  const fake = fixture(); fake.management.legal = [legalEntity]; fake.mount()
  expect(await screen.findByText(/RUC UNIT-NO-FISCAL/)).toBeInTheDocument()
  await first(false)
  await screen.findByRole('heading', { name: 'Inicio de la sede' })
  expect(fake.writes().map(([route]) => route)).toEqual([path + '/branches'])
  expect(fake.management.legal).toHaveLength(1)
})
it('keeps the confirmed entity after rejection and reload, then only posts the corrected branch', async () => {
  const fake = fixture(); fake.state.branchFailure = 409; const view = fake.mount()
  await first()
  await screen.findByText(/ya alcanzó el cupo/)
  expect(fake.management.legal).toHaveLength(1)
  expect(fake.management.branches).toHaveLength(0)
  expect(fake.writes()).toHaveLength(2)
  view.unmount()
  fake.state.branchFailure = 0
  fake.mount()
  expect(await screen.findByText(/RUC QA-NO-FISCAL/)).toBeInTheDocument()
  await first(false)
  await screen.findByRole('heading', { name: 'Inicio de la sede' })
  expect(fake.writes().map(([route]) => route)).toEqual([path + '/legal-entities', path + '/branches', path + '/branches'])
})

it.each(['legal', 'branch'])('keeps the original %s intent across reload and reconciles only after explicit review', async kind => {
  const fake = fixture(); fake.state.lost = kind; const view = fake.mount()
  await first()
  await screen.findByRole('heading', { name: 'No pudimos confirmar si se guardó' })
  await waitFor(() => expect(screen.getByTestId('address')).toHaveTextContent('settingsPending=' + kind))
  const address = screen.getByTestId('address').textContent!
  expect(fake.writes()).toHaveLength(kind === 'legal' ? 1 : 2)
  view.unmount()
  fake.mount(address)
  const retry = await screen.findByRole('button', { name: 'Consultar resultado' })
  fireEvent.click(retry)
  const rows = await screen.findByRole('combobox', { name: 'Registro que quieres verificar' })
  const row = kind === 'legal' ? fake.management.legal[0]! : fake.management.branches[0]!
  await within(rows).findByRole('option', { name: /Synthetic QA/ })
  fireEvent.change(rows, { target: { value: row.id } })
  expect(screen.getByRole('button', { name: 'Confirmar resultado revisado' })).toBeDisabled()
  fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué que este es el registro/ }))
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar resultado revisado' }))
  await waitFor(() => expect(screen.getByTestId('address')).not.toHaveTextContent('settingsPending'))
  expect(fake.writes()).toHaveLength(kind === 'legal' ? 1 : 2)
})

it.each(['cashier', 'restricted-license'] as const)('denies %s before private directory reads or writes', async scenario => {
  const fake = fixture()
  if (scenario === 'cashier') fake.state.role = 'cashier'
  else fake.state.allowed = false
  fake.mount()
  await screen.findByText(scenario === 'cashier' ? /Solo el propietario/ : /no permite operar/)
  expect(fake.fetcher.mock.calls.some(([route]) => /\/license$|\/legal-entities|\/branches/.test(String(route)))).toBe(false)
  expect(fake.writes()).toHaveLength(0)
})
it('rejects a different legal record during reconciliation and preserves the pending intent', async () => {
  const fake = fixture(); fake.state.lost = 'legal'; fake.mount()
  await first()
  await screen.findByRole('heading', { name: 'No pudimos confirmar si se guardó' })
  fake.management.legal.push({ ...legalEntity, ruc: 'OTHER-UNIT-ID' })
  const lookup = screen.getByRole('button', { name: 'Consultar resultado' })
  await waitFor(() => expect(lookup).toBeEnabled())
  fireEvent.click(lookup)
  const rows = await screen.findByRole('combobox', { name: 'Registro que quieres verificar' })
  await within(rows).findByRole('option', { name: /OTHER-UNIT-ID/ })
  fireEvent.change(rows, { target: { value: legalEntity.id } })
  fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué que este es el registro/ }))
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar resultado revisado' }))
  await screen.findByText(/no corresponden a la solicitud original/)
  expect(screen.getByTestId('address')).toHaveTextContent('settingsPending=legal')
  expect(fake.writes()).toHaveLength(1)
})

it('blocks a foreign entity acknowledgement and never submits the branch', async () => {
  const fake = fixture(); fake.state.mismatch = 'legal'; fake.mount()
  await first()
  await screen.findByRole('heading', { name: 'No pudimos confirmar si se guardó' })
  expect(fake.writes().map(([route]) => route)).toEqual([path + '/legal-entities'])
  await waitFor(() => expect(screen.getByTestId('address')).toHaveTextContent('settingsPending=legal'))
})
it('preserves the confirmed branch if the permission read fails and retries only access', async () => {
  const fake = fixture(); fake.state.accessFailure = 503; fake.mount()
  await first()
  expect(await screen.findByText('Sucursal creada.')).toBeInTheDocument()
  const retry = await screen.findByRole('button', { name: 'Volver a intentar' })
  expect(screen.getByTestId('address')).not.toHaveTextContent('settingsPending')
  fake.state.accessFailure = 0
  fireEvent.click(retry)
  await screen.findByRole('heading', { name: 'Inicio de la sede' })
  expect(fake.writes()).toHaveLength(2)
})
it('refuses to send a write if the cookie now represents a different Owner on the same botica', async () => {
  const fake = fixture(); fake.mount()
  await screen.findByRole('textbox', { name: 'Razón social' })
  fake.state.actor = id(801)
  await first()
  await screen.findByText(/Tu acceso cambió/)
  expect(fake.management.legal).toHaveLength(0)
  expect(fake.writes()).toHaveLength(0)
})

it('rechecks zero branches under the writer lock and stops if another tab already created the first', async () => {
  const fake = fixture(); fake.management.legal = [legalEntity]; fake.mount()
  await screen.findByRole('textbox', { name: 'Nombre de la sucursal' })
  fake.management.branches = [workspace.branch]
  await first(false)
  await screen.findByText(/La estructura cambió desde la última consulta/)
  expect(screen.getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', root + '/sucursales/' + workspace.branchId)
  expect(fake.writes()).toHaveLength(0)
})

it('returns an Owner with a pending first branch to the original operation before automatic entry', async () => {
  const fake = fixture(); fake.state.lost = 'branch'; const view = fake.mount()
  await first()
  await screen.findByRole('heading', { name: 'No pudimos confirmar si se guardó' })
  view.unmount()

  fake.mount('', root + '/sucursales')
  await screen.findByRole('heading', { name: 'No pudimos confirmar si se guardó' })
  expect(screen.queryByRole('heading', { name: 'Inicio de la sede' })).not.toBeInTheDocument()
  expect(fake.writes()).toHaveLength(2)
})

it('retains the exact fiscal acknowledgement if saving the recovery state fails', async () => {
  const fake = fixture(); const view = fake.mount()
  const setItem = Storage.prototype.setItem
  const store = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(function (this: Storage, key, value) {
    if (this === localStorage && key.startsWith('medipos-structure-v1/')) {
      const state = JSON.parse(value)
      if (state.entityId && (!state.pending || state.pending.acknowledgedId)) throw new Error('Synthetic storage write failure')
    }
    setItem.call(this, key, value)
  })
  try {
    await first()
    await waitFor(() => expect(screen.getByTestId('address')).toHaveTextContent('settingsCreated=' + fake.management.legal[0]!.id))
    expect(fake.writes()).toHaveLength(1)
  } finally { store.mockRestore() }

  const address = screen.getByTestId('address').textContent!
  view.unmount()
  fake.mount(address)
  fireEvent.click(await screen.findByRole('button', { name: 'Consultar resultado' }))
  await screen.findByText(/Registro encontrado. Synthetic QA entity/)
  expect(screen.queryByRole('combobox', { name: 'Registro que quieres verificar' })).not.toBeInTheDocument()
  expect(fake.writes()).toHaveLength(1)
})

it('retains the exact fiscal acknowledgement when publishing it to the cache fails', async () => {
  const fake = fixture(); fake.mount()
  const setQueryData = QueryClient.prototype.setQueryData
  const cache = vi.spyOn(QueryClient.prototype, 'setQueryData').mockImplementation(function (this: QueryClient, key, value, options) {
    if (key.includes('legal') && fake.management.legal.length) throw new Error('Synthetic directory cache failure')
    return setQueryData.call(this, key, value, options)
  })
  try {
    await first()
    await screen.findByRole('heading', { name: 'Revisa el registro original' })
    await waitFor(() => expect(screen.getByTestId('address')).toHaveTextContent('settingsCreated=' + fake.management.legal[0]!.id))
    expect(fake.writes()).toHaveLength(1)
  } finally { cache.mockRestore() }
})

it('cancels a branch directory read started during the write before publishing the confirmed branch', async () => {
  const fake = fixture()
  fake.management.legal = [legalEntity]
  let allowPost!: () => void
  fake.state.beforeBranch = new Promise(resolve => { allowPost = resolve })
  fake.mount()
  await first(false)
  await waitFor(() => expect(fake.writes()).toHaveLength(1))

  const before = fake.fetcher.mock.calls.length
  let finishRead!: (result: Response) => void
  fake.state.staleBranches = new Promise(resolve => { finishRead = resolve })
  await refocus()
  await waitFor(() => expect(fake.fetcher.mock.calls.slice(before).some(([url, init]) =>
    init?.method === 'GET' && String(url).includes('/branches?'))).toBe(true))
  const signal = fake.fetcher.mock.calls.slice(before).find(([url, init]) =>
    init?.method === 'GET' && String(url).includes('/branches?'))![1]!.signal!
  allowPost()
  await screen.findByRole('heading', { name: 'Inicio de la sede' })
  expect(signal.aborted).toBe(true)
  finishRead(json([]))
  expect(fake.writes()).toHaveLength(1)
})

it('revalidates the verified Owner before clearing the original recovery marker', async () => {
  const fake = fixture(); fake.state.lost = 'legal'; fake.mount()
  await first()
  fireEvent.click(await screen.findByRole('button', { name: 'Consultar resultado' }))
  const rows = await screen.findByRole('combobox', { name: 'Registro que quieres verificar' })
  await within(rows).findByRole('option', { name: /Synthetic QA/ })
  fireEvent.change(rows, { target: { value: fake.management.legal[0]!.id } })
  fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué que este es el registro/ }))
  fake.state.actor = id(801)
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar resultado revisado' }))
  await screen.findByText(/Tu acceso cambió/)
  expect(screen.getByTestId('address')).toHaveTextContent('settingsPending=legal')
  expect(fake.writes()).toHaveLength(1)
})

it('keeps a legacy recovery URL blocked with no automatic retry', async () => {
  const fake = fixture(); fake.mount('?settingsPending=legal')
  await screen.findByRole('heading', { name: 'No pudimos confirmar si se guardó' })
  expect(await screen.findByRole('button', { name: 'Crear sucursal y comenzar' })).toBeDisabled()
  expect(fake.writes()).toHaveLength(0)
})
it('isolates a known pending address from a different account without clearing the original record', async () => {
  const fake = fixture(); fake.state.lost = 'legal'; const view = fake.mount()
  await first()
  await screen.findByRole('heading', { name: 'No pudimos confirmar si se guardó' })
  await waitFor(() => expect(screen.getByTestId('address')).toHaveTextContent('settingsPending=legal'))
  const address = screen.getByTestId('address').textContent!
  view.unmount()
  fake.state.actor = id(801)
  fake.mount(address)
  await screen.findByRole('heading', { name: 'Configura tu botica' })
  await waitFor(() => expect(screen.getByTestId('address')).not.toHaveTextContent('settingsPending'))
  expect(screen.queryByRole('heading', { name: 'No pudimos confirmar si se guardó' })).not.toBeInTheDocument()
  expect(Object.keys(localStorage).filter(key => key.startsWith('medipos-structure-v1/'))).toHaveLength(1)
  expect(fake.writes()).toHaveLength(1)
})
