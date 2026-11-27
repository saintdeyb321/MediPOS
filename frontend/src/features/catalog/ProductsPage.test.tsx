import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, expect, it, vi } from 'vitest'
import { MemoryRouter, useLocation } from 'react-router'
import { RuntimeProvider } from '../../app/providers/runtime'
import { WorkspaceContext } from '../access'
import type { Workspace } from '../access'
import { createPreviewService } from '../../development/fixtures'
import { HttpClient } from '../../shared/api/http'
import { refocus } from '../../test/refocus'
import { JsonNumber, parseExactJson, stringifyExactJson } from '../../shared/api/exactJson'
import { ProductsPage } from './ProductsPage'
import type { BusinessProduct, GlobalProduct, LocalProductInput, PricesInput, ProductUnit, UnitChange } from './api'

// Synthetic transport/component data only. No PostgreSQL, OIDC or commercial operation is executed here.
beforeEach(() => sessionStorage.clear())
const id = (value: string) => `019a0162-0000-7000-8000-${value.padStart(12, '0')}`
const n = (value: string) => new JsonNumber(value)
const tenantId = id('2'); const branchId = id('5'); const timestamp = '2026-10-16T12:00:00Z'
const record: BusinessProduct = { id: id('10'), tenantId, globalProductId: null, internalCode: 'UNIT', productType: 'retail', name: 'Producto unitario', categoryId: id('8'),
  brandOrLaboratory: 'Marca unitaria', barcode: null, medicine: null, retailPrice: n('1.0000'), wholesalePrice: null, isActive: true, createdAt: timestamp }
const base: ProductUnit = { id: id('11'), name: 'Unidad', conversionToBase: n('1.000000000000'), isBaseUnit: true, isActive: true }
const box: ProductUnit = { id: id('12'), name: 'Caja de 10', conversionToBase: n('10.000000000000'), isBaseUnit: false, isActive: true }
const workspace: Workspace = { tenantId, branchId, tenantName: 'Negocio unitario', base: `/boticas/${tenantId}/sucursales/${branchId}`,
  session: { userId: id('1') }, branch: { id: branchId, tenantId, legalEntityId: id('4'), name: 'Sede unitaria', isMainHub: false, createdAt: timestamp },
  access: { userId: id('1'), tenantId, branchId, membershipId: id('3'), role: 'owner', membershipIsActive: true, branchAssigned: true, withinSchedule: true,
    licenseStatus: 'trial', licenseStartsAt: null, licenseExpiresAt: null, evaluatedAtUtc: timestamp } }
function json(data: unknown, status = 200) { return new Response(stringifyExactJson(data), { status, headers: { 'Content-Type': 'application/json' } }) }
function server() {
  const state = { products: [{ ...record }], units: [base, box], unitVersion: 17, loseUnits: false, globals: [] as GlobalProduct[], loseCreate: false, losePrices: false, rejectStatus: false, failList: false, failCategories: false }
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const path = String(input); const method = init?.method ?? 'GET'
    if (path === '/auth/csrf') return json({ requestToken: 'unit-csrf' })
    if (path.includes('/categories?')) return state.failCategories ? json({ code: 'server.unavailable' }, 503) : json([{ id: id('8'), name: 'Categoría unitaria', isActive: true, createdAt: timestamp }])
    if (path.includes('/global-products?')) return json(state.globals)
    if (path.includes('/products?')) {
      if (state.failList) return json({ code: 'server.unavailable' }, 503)
      const params = new URL(path, 'https://unit.test').searchParams
      const offset = Number(params.get('offset')); const limit = Number(params.get('limit'))
      const q = params.get('q')?.toLowerCase(); const category = params.get('categoryId'); const status = params.get('status')
      return json(state.products.filter(row => (!category || row.categoryId === category) &&
        (!status || status === 'all' || row.isActive === (status === 'active')) &&
        (!q || [row.name, row.internalCode, row.brandOrLaboratory, ...(row.medicine?.activeIngredients ?? [])].some(field => field.toLowerCase().includes(q))))
        .slice(offset, offset + limit))
    }
    if (method === 'POST' && path.endsWith('/products/local')) {
      const data = parseExactJson(String(init?.body)) as LocalProductInput
      const created: BusinessProduct = { ...data, id: id('20'), tenantId, globalProductId: null, createdAt: timestamp, medicine: data.medicine ? { ...data.medicine, activeIngredients: data.medicine.components.map(row => row.ingredient), normalizedStrength: data.medicine.components.map(row => row.strengthNormalized).join(' + ') } : null }
      state.products.push(created)
      if (state.loseCreate) throw new TypeError('Unit-test lost response')
      return json(created, 201)
    }
    if (method === 'POST' && path.endsWith('/products/from-global')) {
      const data = parseExactJson(String(init?.body)) as { globalProductId: string; internalCode: string; retailPrice: JsonNumber; wholesalePrice: JsonNumber | null; isActive: boolean }
      const source = state.globals.find(row => row.id === data.globalProductId)!
      const created = { ...source, ...data, id: id('20'), tenantId, createdAt: timestamp }
      state.products.push(created); return json(created, 201)
    }
    const match = /\/products\/([^/?]+)(?:\/(prices|status|unit-configuration))?$/.exec(path)
    if (match) {
      const current = state.products.find(row => row.id === match[1])
      if (!current) return json({ code: 'catalog.product_not_found' }, 404)
      if (method === 'GET') return match[2] === 'unit-configuration' ? json({ version: state.unitVersion, units: current.id === record.id ? state.units : [] }) : json({ product: current, revision: '17', units: current.id === record.id ? state.units : [] })
      if (match[2] === 'prices') {
        Object.assign(current, parseExactJson(String(init?.body)) as PricesInput)
        if (state.losePrices) throw new TypeError('Unit-test lost price response')
        return json(current)
      }
      if (match[2] === 'status') {
        if (state.rejectStatus) return json({ code: 'access.role_denied' }, 403)
        current.isActive = (parseExactJson(String(init?.body)) as { isActive: boolean }).isActive
        return json(current)
      }
      if (match[2] === 'unit-configuration') {
        const data = parseExactJson(String(init?.body)) as { expectedVersion: JsonNumber; units: UnitChange[] }
        if (data.expectedVersion.literal !== String(state.unitVersion)) return json({ code: 'catalog.concurrent_change' }, 409)
        state.units = data.units.map((row, index) => ({ ...row, id: row.id ?? id(String(30 + index)) })); state.unitVersion++
        if (state.loseUnits) throw new TypeError('Synthetic lost unit response')
        return json({ version: state.unitVersion, units: state.units })
      }
    }
    throw new Error('Unexpected catalog test route')
  })
  return { state, fetcher, writes: () => fetcher.mock.calls.filter(([, init]) => init?.method === 'POST' || init?.method === 'PUT') }
}
function Location() { return <output data-testid="location">{useLocation().search}</output> }
function mount(fake: ReturnType<typeof server>, search = '', context = workspace) {
  const tree = (current: Workspace) => <RuntimeProvider runtime={{ service: createPreviewService('owner'), http: new HttpClient(fake.fetcher), preview: false }}>
    <MemoryRouter initialEntries={['/productos' + search]}><WorkspaceContext.Provider value={current}><ProductsPage /><Location /></WorkspaceContext.Provider></MemoryRouter>
  </RuntimeProvider>
  const view = render(tree(context))
  return { ...view, changeContext: (current: Workspace) => view.rerender(tree(current)) }
}
function showList() { fireEvent.click(screen.getByRole('button', { name: 'Lista' })) }
async function selectProduct() { showList(); fireEvent.click(await screen.findByRole('button', { name: 'Ver Producto unitario' })); return screen.findByRole('region', { name: 'Detalle del producto' }) }
async function openTask(task: 'prices' | 'units' | 'status') {
  const detail = within(await selectProduct())
  if (task === 'prices') fireEvent.click(detail.getByRole('button', { name: 'Precios' }))
  if (task === 'units') fireEvent.click(detail.getByRole('button', { name: 'Presentaciones' }))
  if (task === 'status') fireEvent.click(detail.getByRole('button', { name: 'Datos' }))
  fireEvent.click(detail.getByRole('button', { name: { prices: 'Editar precios', units: 'Configurar presentaciones', status: 'Desactivar producto' }[task] }))
}
function nextStep(form: ReturnType<typeof within>) { fireEvent.click(form.getByRole('button', { name: 'Continuar' })) }
function fillPrice(form: ReturnType<typeof within>, value: string, advance = true) {
  fireEvent.change(form.getByRole('textbox', { name: 'Precio minorista por unidad base (S/)' }), { target: { value } })
  if (advance) nextStep(form)
}
async function fillLocal(type: 'retail' | 'medicine' = 'retail', advance = true) {
  const button = screen.getByRole('button', { name: 'Nuevo producto' })
  await waitFor(() => expect(button).toBeEnabled()); fireEvent.click(button)
  const form = within(await screen.findByRole('form', { name: 'Crear producto local' }))
  fireEvent.change(form.getByRole('textbox', { name: 'Código interno' }), { target: { value: 'UNIT-NEW' } })
  fireEvent.change(form.getByRole('combobox', { name: 'Tipo de producto' }), { target: { value: type } })
  fireEvent.change(form.getByRole('textbox', { name: 'Nombre del producto' }), { target: { value: 'Nuevo producto unitario' } })
  nextStep(form)
  fireEvent.change(form.getByRole('combobox', { name: 'Categoría' }), { target: { value: id('8') } })
  fireEvent.change(form.getByRole('textbox', { name: 'Marca o laboratorio documentado' }), { target: { value: 'Marca documentada unitaria' } })
  if (type === 'retail') fillPrice(form, '1.2345', advance)
  return form
}
function expectNoCreationIntent(fake: ReturnType<typeof server>) {
  expect(fake.writes()).toHaveLength(0)
  expect(screen.getByTestId('location')).not.toHaveTextContent('catalogPending')
  expect(sessionStorage.getItem(`medipos-catalog-creation/${workspace.session.userId}/${tenantId}/${branchId}/${workspace.access.membershipId}`)).toBeNull()
}
function reviewWithoutSubmitting(form: ReturnType<typeof within>, confirm: string) {
  const advance = form.getByRole('button', { name: 'Continuar' })
  const click = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 })
  expect(advance).toHaveAttribute('type', 'button')
  fireEvent(advance, click)
  const submit = form.getByRole('button', { name: confirm })
  expect({
    cancelled: click.defaultPrevented,
    sameNode: submit === advance,
    advanceTypeAfterClick: advance.getAttribute('type'),
    submitType: submit.getAttribute('type'),
  }).toEqual({ cancelled: true, sameNode: false, advanceTypeAfterClick: 'button', submitType: 'submit' })
  return submit
}
it('lists real-shaped records without inventing stock and reads presentations/prices from the scoped detail', async () => {
  const fake = server(); mount(fake)
  showList()
  expect(await screen.findByRole('cell', { name: 'Categoría unitaria' })).toBeInTheDocument()
  expect(screen.getByRole('cell', { name: /Venta general/ })).toBeInTheDocument()
  const detail = within(await selectProduct())
  fireEvent.click(detail.getByRole('button', { name: 'Presentaciones' }))
  expect(detail.getByText(/Precio minorista por presentación: S\/ 10.00/)).toBeInTheDocument()
  fireEvent.click(detail.getByRole('button', { name: 'Configurar presentaciones' }))
  const form = within(await screen.findByRole('form', { name: 'Guardar configuración de presentaciones' }))
  expect(form.getByRole('textbox', { name: 'Conversión a base 2' })).toBeDisabled()
  expect(form.getByRole('button', { name: 'Quitar presentación nueva 2' })).toBeDisabled()
  expect(screen.queryByText('1.0000')).not.toBeInTheDocument()
  expect(fake.writes()).toHaveLength(0)
})
it('paginates with backend offset/limit and preserves true empty pages', async () => {
  const fake = server(); fake.state.products = Array.from({ length: 51 }, (_, index) => ({ ...record, id: id(String(100 + index)), name: `Fila unitaria ${index}` }))
  mount(fake); showList()
  await screen.findByRole('button', { name: 'Ver Fila unitaria 0' })
  fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }))
  await screen.findByRole('button', { name: 'Ver Fila unitaria 50' })
  expect(screen.queryByRole('button', { name: 'Ver Fila unitaria 0' })).not.toBeInTheDocument()
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/products?offset=50&limit=50'))).toBe(true)
})
it('finds products beyond the original page, combines server filters and resets pagination', async () => {
  const fake = server()
  fake.state.products = Array.from({ length: 51 }, (_, index) => ({ ...record, id: id(String(100 + index)), name: `Fila unitaria ${index}` }))
  fake.state.products[50] = { ...fake.state.products[50]!, brandOrLaboratory: 'Marca %_ especial', isActive: false }
  mount(fake)
  await screen.findByRole('button', { name: 'Ver Fila unitaria 0' })
  expect(screen.queryByRole('button', { name: 'Ver Fila unitaria 50' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }))
  await screen.findByRole('button', { name: 'Ver Fila unitaria 50' })
  const reads = () => fake.fetcher.mock.calls.map(([path]) => String(path)).filter(path => path.includes('/products?'))
  const count = reads().length
  fireEvent.change(screen.getByRole('searchbox', { name: 'Buscar en mis productos' }), { target: { value: '%_' } })
  fireEvent.change(screen.getByRole('combobox', { name: 'Categoría' }), { target: { value: id('8') } })
  fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'inactive' } })
  expect(reads()).toHaveLength(count)
  fireEvent.click(screen.getByRole('button', { name: 'Aplicar filtros' }))
  await waitFor(() => expect(reads().at(-1)).toContain(`offset=0&limit=50&q=%25_&categoryId=${id('8')}&status=inactive`))
  await screen.findByRole('button', { name: 'Ver Fila unitaria 50' })
  expect(screen.queryByRole('navigation', { name: 'Paginación' })).not.toBeInTheDocument()
  fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'active' } })
  fireEvent.click(screen.getByRole('button', { name: 'Aplicar filtros' }))
  await screen.findByText('Sin coincidencias en mis productos')
  fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }))
  await screen.findByRole('button', { name: 'Ver Fila unitaria 0' })
  expect(fake.writes()).toHaveLength(0)
})
it('creates a local general product only after review, serializes exact prices and refreshes the selected product once', async () => {
  const fake = server(); mount(fake); const form = await fillLocal('retail', false)
  reviewWithoutSubmitting(form, 'Guardar producto')
  expectNoCreationIntent(fake)
  fireEvent.click(form.getByRole('button', { name: 'Anterior' }))
  expect(form.getByRole('textbox', { name: 'Precio minorista por unidad base (S/)' })).toHaveValue('1.2345')
  expectNoCreationIntent(fake)
  const button = reviewWithoutSubmitting(form, 'Guardar producto')
  expect(button).toBeEnabled()
  expectNoCreationIntent(fake)
  fireEvent.click(button); fireEvent.click(button)
  await screen.findAllByText(/Operación guardada y producto consultado/)
  expect(fake.writes()).toHaveLength(1)
  expect(fake.writes()[0]?.[1]?.body).toContain('"retailPrice":1.2345')
  expect(fake.writes()[0]?.[1]?.body).toContain('"medicine":null')
  expect(new Headers(fake.writes()[0]?.[1]?.headers).get('X-CSRF-TOKEN')).toBe('unit-csrf')
  expect(await form.findByRole('button', { name: 'Producto guardado' })).toBeDisabled()
  expect(fake.state.products.find(row => row.internalCode === 'UNIT-NEW')?.name).toBe('Nuevo producto unitario')
  expect(screen.getByRole('button', { name: 'Configurar presentaciones del producto guardado' })).toBeEnabled()
})
it('requires structured documented medicine fields and preserves microgram/spelling payloads', async () => {
  const fake = server(); mount(fake); const form = await fillLocal('medicine')
  expect(form.getByRole('button', { name: 'Continuar' })).toBeDisabled()
  fireEvent.change(form.getByRole('textbox', { name: 'Principio activo 1' }), { target: { value: 'Ingrediente sintético de prueba' } })
  fireEvent.change(form.getByRole('textbox', { name: 'Concentración documentada 1' }), { target: { value: '25 µg' } })
  fireEvent.change(form.getByRole('textbox', { name: 'Forma farmacéutica' }), { target: { value: 'Forma de prueba' } })
  fillPrice(form, '1.2345')
  expect(form.getByRole('button', { name: 'Guardar producto' })).toBeDisabled()
  expectNoCreationIntent(fake)
  fireEvent.click(form.getByRole('checkbox', { name: /Verifiqué los atributos del medicamento/ }))
  expectNoCreationIntent(fake)
  fireEvent.click(form.getByRole('button', { name: 'Guardar producto' }))
  await screen.findAllByText(/Operación guardada y producto consultado/)
  expect(fake.writes()).toHaveLength(1)
  expect(fake.writes()[0]?.[1]?.body).toContain('"components":[{"ingredient":"Ingrediente sintético de prueba","strengthNormalized":"25 µg"}]')
})
it('shows an empty global catalog honestly and associates a selected reference without inventory writes', async () => {
  const fake = server(); mount(fake)
  fireEvent.click(screen.getByRole('button', { name: 'Catálogo global' }))
  await screen.findByText('No hay productos globales en esta página.')
  const businessReads = () => fake.fetcher.mock.calls.filter(([path]) => String(path).includes('/products?')).length
  const before = businessReads()
  fake.state.globals = [{ ...record, id: id('90'), name: 'Referencia unitaria' }]
  await refocus()
  fireEvent.click(await screen.findByRole('button', { name: 'Añadir Referencia unitaria a mi botica' }))
  expect(businessReads()).toBe(before)
  const form = within(screen.getByRole('form', { name: 'Asociar producto global' }))
  fireEvent.change(form.getByRole('textbox', { name: 'Código interno' }), { target: { value: 'GLOBAL-UNIT' } })
  fillPrice(form, '2.00', false)
  reviewWithoutSubmitting(form, 'Asociar al negocio')
  expectNoCreationIntent(fake)
  fireEvent.click(form.getByRole('button', { name: 'Anterior' }))
  expectNoCreationIntent(fake)
  const user = userEvent.setup()
  form.getByRole('button', { name: 'Continuar' }).focus()
  await user.keyboard('{Enter}')
  const confirm = form.getByRole('button', { name: 'Asociar al negocio' })
  expect(confirm).toBeEnabled()
  expectNoCreationIntent(fake)
  confirm.focus()
  await user.keyboard('{Enter}')
  await screen.findAllByText(/Operación guardada y producto consultado/)
  expect(fake.writes()).toHaveLength(1)
  expect(String(fake.writes()[0]?.[0])).toMatch(/\/products\/from-global$/)
  expect(fake.state.products[1]?.globalProductId).toBe(id('90'))
})
it('retains uncertain creation across reload and never resends it automatically', async () => {
  const fake = server(); fake.state.loseCreate = true; const view = mount(fake); const form = await fillLocal()
  fireEvent.click(form.getByRole('button', { name: 'Guardar producto' }))
  await screen.findByText('No pudimos completar la operación de catálogo')
  const search = screen.getByTestId('location').textContent!
  expect(search).toContain('catalogPending=create')
  const pendingTitle = 'Operación pendiente de conciliación'
  expect(screen.getAllByText(pendingTitle, { exact: true })).toHaveLength(1)
  const creationDialog = () => within(screen.getByRole('dialog', { name: 'Nuevo producto' }))
  expect(creationDialog().getByRole('heading', { name: pendingTitle })).toBeVisible()
  fireEvent.click(creationDialog().getByRole('button', { name: /^Volver a la página$/ }))
  expect(screen.queryByRole('dialog', { name: 'Nuevo producto' })).not.toBeInTheDocument()
  expect(screen.getAllByText(pendingTitle, { exact: true })).toHaveLength(1)
  fireEvent.click(screen.getByRole('button', { name: 'Revisar solicitud pendiente sin reenviar' }))
  expect(creationDialog().getByRole('button', { name: 'Guardar producto' })).toBeDisabled()
  expect(creationDialog().getByRole('heading', { name: pendingTitle })).toBeVisible()
  expect(screen.getByTestId('location')).toHaveTextContent('catalogPending=create')
  expect(fake.writes()).toHaveLength(1)
  view.unmount(); mount(fake)
  expect(screen.getByRole('button', { name: 'Nuevo producto' })).toBeDisabled()
  showList(); await screen.findByRole('button', { name: 'Ver Nuevo producto unitario' })
  const reconcile = async (name: string, code: string) => {
    fireEvent.click(screen.getByRole('button', { name: 'Ver ' + name }))
    fireEvent.click(screen.getByRole('button', { name: 'Cerrar Detalle del producto' }))
    fireEvent.click(screen.getByRole('button', { name: 'Consultar producto de la operación' }))
    await screen.findAllByText(new RegExp('Producto consultado: ' + name))
    fireEvent.change(screen.getByRole('textbox', { name: 'Código interno de la solicitud original' }), { target: { value: code } })
    fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué el código/ }))
    return screen.getByRole('button', { name: 'Continuar tras conciliación verificada' })
  }
  expect(await reconcile('Producto unitario', 'UNIT')).toBeDisabled()
  const detailPath = `/api/tenants/${tenantId}/branches/${branchId}/products/${id('20')}`
  const defaultFetch = fake.fetcher.getMockImplementation()!
  let resolveDetail!: () => void
  let failConsult!: (response: Response) => void
  const detailInFlight = new Promise<void>(resolve => { resolveDetail = resolve })
  const consultInFlight = new Promise<Response>(resolve => { failConsult = resolve })
  let reads = 0
  fake.fetcher.mockImplementation(async (input, init) => {
    if (String(input) === detailPath && init?.method === 'GET') {
      reads++
      if (reads === 1) await detailInFlight
      if (reads === 2) return consultInFlight
    }
    return defaultFetch(input, init)
  })
  fireEvent.click(screen.getByRole('button', { name: 'Ver Nuevo producto unitario' }))
  await waitFor(() => expect(reads).toBe(1))
  fireEvent.click(screen.getByRole('button', { name: 'Cerrar Detalle del producto' }))
  const consult = screen.getByRole('button', { name: 'Consultar producto de la operación' })
  const create = screen.getByRole('button', { name: 'Nuevo producto' })
  const storageKey = `medipos-catalog-creation/${workspace.session.userId}/${tenantId}/${branchId}/${workspace.access.membershipId}`
  const reviewedProduct = /Producto consultado: Nuevo producto unitario · Código UNIT-NEW/
  fireEvent.click(consult)
  await waitFor(() => expect(reads).toBe(2))
  await act(async () => { resolveDetail(); await detailInFlight })
  expect(screen.queryByText(reviewedProduct)).not.toBeInTheDocument()
  expect(consult).toBeDisabled()
  expect(create).toBeDisabled()
  expect(sessionStorage.getItem(storageKey)).toBe('UNIT-NEW')
  failConsult(json({ code: 'server.unavailable' }, 503))
  await screen.findByText('No pudimos completar la operación de catálogo')
  expect(screen.queryByText(reviewedProduct)).not.toBeInTheDocument()
  expect(create).toBeDisabled()
  expect(sessionStorage.getItem(storageKey)).toBe('UNIT-NEW')

  fireEvent.click(consult)
  const recoveryPanel = within(consult.parentElement!)
  await recoveryPanel.findByText(reviewedProduct)
  expect(reads).toBe(3)
  const original = screen.getByRole('button', { name: 'Continuar tras conciliación verificada' })
  const codeLabel = 'Código interno de la solicitud original'
  expect(screen.getAllByLabelText(codeLabel)).toHaveLength(1)
  const code = screen.getByRole('textbox', { name: codeLabel })
  const reviewed = screen.getByRole('checkbox', { name: /Verifiqué el código/ })
  fireEvent.change(code, { target: { value: 'UNIT' } })
  fireEvent.click(reviewed)
  expect(original).toBeDisabled()
  fireEvent.change(code, { target: { value: 'UNIT-NEW' } })
  expect(original).toBeDisabled()
  fireEvent.click(reviewed)
  expect(original).toBeEnabled()
  expect(sessionStorage.getItem(storageKey)).toBe('UNIT-NEW')
  fireEvent.click(original)
  await waitFor(() => expect(create).toBeEnabled())
  expect(sessionStorage.getItem(storageKey)).toBeNull()
  expect(screen.getByTestId('location')).not.toHaveTextContent('catalogPending')
  expect(fake.writes()).toHaveLength(1)
})
it('updates prices and status through their official endpoints, refetching actual detail', async () => {
  const fake = server(); mount(fake); await openTask('prices')
  const form = within(screen.getByRole('form', { name: 'Actualizar precios' }))
  fireEvent.change(form.getByRole('textbox', { name: 'Precio minorista por unidad base (S/)' }), { target: { value: '2.1234' } })
  fireEvent.click(form.getByRole('button', { name: 'Guardar precios' }))
  await screen.findAllByText(/Operación guardada y producto consultado/)
  const prices = screen.getByRole('dialog', { name: 'Editar precios' })
  await waitFor(() => expect(within(prices).getByRole('textbox', { name: 'Precio minorista por unidad base (S/)' })).toHaveValue('2.1234'))
  fireEvent.click(await within(prices).findByRole('button', { name: 'Cerrar Editar precios' }))
  const discard = within(prices).queryByRole('button', { name: 'Descartar cambios' })
  if (discard) fireEvent.click(discard)
  await openTask('status')
  const status = within(screen.getByRole('dialog', { name: 'Cambiar estado del producto' }))
  fireEvent.click(status.getByRole('button', { name: 'Desactivar producto' }))
  await status.findByRole('button', { name: 'Activar producto' })
  expect(fake.writes()).toHaveLength(2)
  expect(String(fake.writes()[1]?.[0])).toMatch(/\/status$/)
  expect(fake.state.products[0]?.isActive).toBe(false)
})
it('does not retry uncertain prices or unlock after an early read without operator review', async () => {
  const fake = server(); fake.state.losePrices = true; mount(fake); await openTask('prices')
  fireEvent.change(screen.getByRole('textbox', { name: 'Precio minorista por unidad base (S/)' }), { target: { value: '2.00' } })
  fireEvent.click(screen.getByRole('button', { name: 'Guardar precios' }))
  await screen.findAllByText(/Producto consultado: Producto unitario/)
  expect(screen.getByRole('button', { name: 'Guardar precios' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Continuar tras conciliación verificada' })).toBeDisabled()
  expect(fake.writes()).toHaveLength(1)
  const editor = () => within(screen.getByRole('dialog', { name: 'Editar precios' }))
  expect(screen.getAllByText('Operación pendiente de conciliación', { exact: true })).toHaveLength(1)
  fireEvent.change(screen.getByRole('textbox', { name: 'Código interno de la solicitud original' }), { target: { value: 'OTRO-CODIGO' } })
  fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué el código/ }))
  expect(screen.getByRole('button', { name: 'Continuar tras conciliación verificada' })).toBeDisabled()
  fireEvent.click(editor().getByRole('button', { name: /^Volver a la página$/ }))
  expect(screen.queryByRole('dialog', { name: 'Editar precios' })).not.toBeInTheDocument()
  expect(screen.getAllByText('Operación pendiente de conciliación', { exact: true })).toHaveLength(1)
  expect(screen.getByRole('textbox', { name: 'Código interno de la solicitud original' })).toHaveValue('OTRO-CODIGO')
  fireEvent.click(screen.getByRole('button', { name: 'Revisar solicitud pendiente sin reenviar' }))
  expect(editor().getByRole('textbox', { name: 'Código interno de la solicitud original' })).toHaveValue('OTRO-CODIGO')
  expect(editor().getByRole('textbox', { name: 'Precio minorista por unidad base (S/)' })).toHaveValue('2.00')
  expect(editor().getByRole('checkbox', { name: /Verifiqué el código/ })).toBeChecked()
  expect(editor().getByRole('button', { name: 'Continuar tras conciliación verificada' })).toBeDisabled()
  fireEvent.change(screen.getByRole('textbox', { name: 'Código interno de la solicitud original' }), { target: { value: 'UNIT' } })
  fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué el código/ }))
  fireEvent.click(screen.getByRole('button', { name: 'Continuar tras conciliación verificada' }))
  await screen.findAllByText(/El operador revisó el estado consultado/)
  expect(fake.writes()).toHaveLength(1)
})
it('initializes units with the version observed from the server and generated ids, never old PUT', async () => {
  const fake = server(); fake.state.units = []; mount(fake); await openTask('units')
  const form = within(await screen.findByRole('form', { name: 'Guardar configuración de presentaciones' }))
  fireEvent.change(form.getByRole('textbox', { name: 'Nombre de presentación 1' }), { target: { value: 'Unidad' } })
  fireEvent.click(form.getByRole('button', { name: 'Agregar presentación' }))
  fireEvent.change(form.getByRole('textbox', { name: 'Nombre de presentación 2' }), { target: { value: 'Caja' } })
  fireEvent.change(form.getByRole('textbox', { name: 'Conversión a base 2' }), { target: { value: '10' } })
  expect(form.getByRole('button', { name: 'Guardar presentaciones' })).toBeEnabled()
  expect(form.getByRole('textbox', { name: 'Conversión a base 2' })).toHaveValue('10')
  fireEvent.click(form.getByRole('button', { name: 'Guardar presentaciones' }))
  await screen.findAllByText(/Operación guardada/)
  expect(fake.writes()).toHaveLength(1)
  expect(String(fake.writes()[0]?.[0])).toMatch(/\/unit-configuration$/)
  expect(fake.writes()[0]?.[1]?.body).toContain('"expectedVersion":17')
  expect(fake.state.units).toHaveLength(2)
})
it('preserves the reviewed unit revision when a concurrent change rejects the write', async () => {
  const fake = server()
  mount(fake)
  await openTask('units')
  const form = within(await screen.findByRole('form', { name: 'Guardar configuración de presentaciones' }))
  const name = form.getByRole('textbox', { name: 'Nombre de presentación 2' })
  fireEvent.change(name, { target: { value: 'Mi edición' } })
  fake.state.unitVersion = 18
  fake.state.units[1] = { ...box, name: 'Cambio concurrente' }
  expect(screen.getByRole('button', { name: 'Recargar presentaciones del servidor' })).toBeDisabled()
  fireEvent.click(form.getByRole('button', { name: 'Guardar presentaciones' }))
  await screen.findAllByRole('alert')
  expect(name).toHaveValue('Mi edición')
  expect(fake.writes()).toHaveLength(1)
  expect(fake.writes()[0]?.[1]?.body).toContain('"expectedVersion":17')
  expect(fake.state.units[1]?.name).toBe('Cambio concurrente')
})

it('preserves ids on rename/deactivation and reconciles lost unit responses without resending', async () => {
  const fake = server(); fake.state.loseUnits = true; mount(fake); await openTask('units')
  const form = within(await screen.findByRole('form', { name: 'Guardar configuración de presentaciones' }))
  fireEvent.change(form.getByRole('textbox', { name: 'Nombre de presentación 2' }), { target: { value: 'Caja LAB' } })
  fireEvent.click(form.getByRole('checkbox', { name: 'Presentación activa 2' }))
  fireEvent.click(form.getByRole('button', { name: 'Guardar presentaciones' }))
  await screen.findByRole('heading', { name: 'Operación pendiente de conciliación' })
  await waitFor(() => expect(fake.state.units[1]?.name).toBe('Caja LAB'))
  expect(fake.state.units[1]?.id).toBe(box.id); expect(fake.state.units[1]?.isActive).toBe(false)
  expect(fake.writes()).toHaveLength(1)
  expect(screen.getByTestId('location')).toHaveTextContent('catalogPending=units')
})
it('denies a non-owner route without issuing catalog requests', async () => {
  const fake = server(); mount(fake, '', { ...workspace, access: { ...workspace.access, role: 'cashier' } })
  expect(screen.getByRole('heading', { name: 'Productos requiere acceso de propietario' })).toBeInTheDocument()
  expect(fake.fetcher).not.toHaveBeenCalled()
})
it('shows list failures rather than inventing an empty catalog', async () => {
  const fake = server(); fake.state.failList = true; mount(fake)
  await screen.findByText('No pudimos consultar los productos')
  expect(screen.queryByText('No hay productos en esta página.')).not.toBeInTheDocument()
})
it('keeps missing categories distinct from a successful empty catalog and blocks an unreviewed local creation', async () => {
  const fake = server(); fake.state.failCategories = true; mount(fake)
  fireEvent.click(screen.getByRole('button', { name: 'Nuevo producto' }))
  await screen.findByText('No pudimos consultar las categorías')
  expect(screen.queryByRole('form', { name: 'Crear producto local' })).not.toBeInTheDocument()
  expect(fake.writes()).toHaveLength(0)
})
it('handles a definitive authorization rejection without changing status or retrying', async () => {
  const fake = server(); fake.state.rejectStatus = true; mount(fake); await openTask('status')
  fireEvent.click(screen.getByRole('button', { name: 'Desactivar producto' }))
  await screen.findByText('No pudimos completar la operación de catálogo')
  expect(screen.getByRole('button', { name: 'Desactivar producto' })).toBeDisabled()
  expect(fake.state.products[0]?.isActive).toBe(true)
  expect(fake.writes()).toHaveLength(1)
})
it('clears the selected product and form across tenant changes instead of displaying cached private data', async () => {
  const fake = server(); const view = mount(fake); await selectProduct()
  const nextTenant = id('99')
  fake.state.products = [{ ...record, tenantId: nextTenant, name: 'Producto del otro contexto' }]
  const next: Workspace = { ...workspace, tenantId: nextTenant, branch: { ...workspace.branch, tenantId: nextTenant }, access: { ...workspace.access, tenantId: nextTenant } }
  view.changeContext(next); showList()
  expect(screen.queryByRole('region', { name: 'Detalle del producto' })).not.toBeInTheDocument()
  await screen.findByRole('button', { name: 'Ver Producto del otro contexto' })
  expect(screen.queryByRole('button', { name: 'Ver Producto unitario' })).not.toBeInTheDocument()
  expect(fake.writes()).toHaveLength(0)
})
it('does not apply late detail responses after selecting another product', async () => {
  const fake = server(); fake.state.products.push({ ...record, id: id('20'), name: 'Otro producto unitario' })
  const original = fake.fetcher.getMockImplementation()!; let release!: () => void; let signal: AbortSignal | null | undefined
  fake.fetcher.mockImplementation(async (input, init) => {
    if (String(input).endsWith(`/products/${record.id}`)) { signal = init?.signal; await new Promise<void>(resolve => { release = resolve }) }
    return original(input, init)
  })
  mount(fake); showList(); fireEvent.click(await screen.findByRole('button', { name: 'Ver Producto unitario' }))
  await waitFor(() => expect(signal).toBeDefined())
  fireEvent.click(screen.getByRole('button', { name: 'Ver Otro producto unitario' }))
  await screen.findByRole('heading', { name: 'Otro producto unitario' })
  expect(signal?.aborted).toBe(true)
  await act(async () => release())
  expect(within(screen.getByRole('region', { name: 'Detalle del producto' })).queryByRole('heading', { name: 'Producto unitario' })).not.toBeInTheDocument()
})
