import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { MemoryRouter, useLocation } from 'react-router'
import { RuntimeProvider } from '../../app/providers/runtime'
import { WorkspaceContext } from '../access'
import type { Workspace } from '../access'
import { createPreviewService } from '../../development/fixtures'
import { HttpClient } from '../../shared/api/http'
import { JsonNumber, parseExactJson, stringifyExactJson } from '../../shared/api/exactJson'
import { ProductsPage } from './ProductsPage'
import type { BusinessProduct, GlobalProduct, LocalProductInput, PricesInput, ProductUnit, UnitInput } from './api'

// Synthetic transport/component data only. No PostgreSQL, OIDC or commercial operation is executed here.
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
  const state = { products: [{ ...record }], units: [base, box], globals: [] as GlobalProduct[], loseCreate: false, losePrices: false, rejectStatus: false, failList: false, failCategories: false }
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const path = String(input); const method = init?.method ?? 'GET'
    if (path === '/auth/csrf') return json({ requestToken: 'unit-csrf' })
    if (path.includes('/categories?')) return state.failCategories ? json({ code: 'server.unavailable' }, 503) : json([{ id: id('8'), name: 'Categoría unitaria', isActive: true, createdAt: timestamp }])
    if (path.includes('/global-products?')) return json(state.globals)
    if (path.includes('/products?')) {
      if (state.failList) return json({ code: 'server.unavailable' }, 503)
      const offset = Number(new URL(path, 'https://unit.test').searchParams.get('offset'))
      return json(state.products.slice(offset, offset + 50))
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
    const match = /\/products\/([^/?]+)(?:\/(prices|status|units))?$/.exec(path)
    if (match) {
      const current = state.products.find(row => row.id === match[1])
      if (!current) return json({ code: 'catalog.product_not_found' }, 404)
      if (method === 'GET') return json({ product: current, units: current.id === record.id ? state.units : [] })
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
      if (match[2] === 'units') {
        const data = parseExactJson(String(init?.body)) as { units: UnitInput[] }
        state.units = data.units.map((row, index) => ({ ...row, id: id(String(30 + index)) }))
        return json(state.units)
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
async function selectProduct() { fireEvent.click(await screen.findByRole('button', { name: 'Ver Producto unitario' })); return screen.findByRole('region', { name: 'Detalle del producto' }) }
async function fillLocal(type: 'retail' | 'medicine' = 'retail') {
  const button = screen.getByRole('button', { name: 'Nuevo producto local' })
  await waitFor(() => expect(button).toBeEnabled()); fireEvent.click(button)
  const form = within(await screen.findByRole('form', { name: 'Crear producto local' }))
  fireEvent.change(form.getByRole('textbox', { name: 'Código interno' }), { target: { value: 'UNIT-NEW' } })
  fireEvent.change(form.getByRole('combobox', { name: 'Tipo de producto' }), { target: { value: type } })
  fireEvent.change(form.getByRole('textbox', { name: 'Nombre del producto' }), { target: { value: 'Nuevo producto unitario' } })
  fireEvent.change(form.getByRole('combobox', { name: 'Categoría' }), { target: { value: id('8') } })
  fireEvent.change(form.getByRole('textbox', { name: 'Marca o laboratorio documentado' }), { target: { value: 'Marca documentada unitaria' } })
  fireEvent.change(form.getByRole('textbox', { name: 'Precio minorista por unidad base (S/)' }), { target: { value: '1.2345' } })
  return form
}
it('lists real-shaped records without inventing stock and reads presentations/prices from the scoped detail', async () => {
  const fake = server(); mount(fake)
  expect(await screen.findByRole('cell', { name: /Categoría unitaria.*Venta general/ })).toBeInTheDocument()
  const detail = within(await selectProduct())
  expect(detail.getByText(/Precio minorista por presentación: S\/ 10.00/)).toBeInTheDocument()
  expect(detail.getByText(/Reconfiguración bloqueada/)).toBeInTheDocument()
  expect(screen.queryByRole('form', { name: 'Configurar presentaciones iniciales' })).not.toBeInTheDocument()
  expect(screen.queryByText('1.0000')).not.toBeInTheDocument()
  expect(fake.writes()).toHaveLength(0)
})
it('paginates with backend offset/limit and preserves true empty pages', async () => {
  const fake = server(); fake.state.products = Array.from({ length: 51 }, (_, index) => ({ ...record, id: id(String(100 + index)), name: `Fila unitaria ${index}` }))
  mount(fake)
  await screen.findByRole('button', { name: 'Ver Fila unitaria 0' })
  fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }))
  await screen.findByRole('button', { name: 'Ver Fila unitaria 50' })
  expect(screen.queryByRole('button', { name: 'Ver Fila unitaria 0' })).not.toBeInTheDocument()
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/products?offset=50&limit=50'))).toBe(true)
})
it('creates a local general product only after review, serializes exact prices and refreshes the selected product once', async () => {
  const fake = server(); mount(fake); const form = await fillLocal()
  const button = form.getByRole('button', { name: 'Crear producto local' })
  expect(button).toBeDisabled(); expect(fake.writes()).toHaveLength(0)
  fireEvent.click(form.getByRole('checkbox', { name: /Revisé la identidad/ }))
  fireEvent.click(button); fireEvent.click(button)
  await screen.findByText(/Operación guardada y producto consultado/)
  expect(fake.writes()).toHaveLength(1)
  expect(fake.writes()[0]?.[1]?.body).toContain('"retailPrice":1.2345')
  expect(fake.writes()[0]?.[1]?.body).toContain('"medicine":null')
  expect(new Headers(fake.writes()[0]?.[1]?.headers).get('X-CSRF-TOKEN')).toBe('unit-csrf')
  expect(screen.queryByRole('form', { name: 'Crear producto local' })).not.toBeInTheDocument()
  expect(await screen.findByRole('region', { name: 'Detalle del producto' })).toHaveTextContent('Nuevo producto unitario')
})
it('requires structured documented medicine fields and preserves microgram/spelling payloads', async () => {
  const fake = server(); mount(fake); const form = await fillLocal('medicine')
  fireEvent.click(form.getByRole('checkbox', { name: /Revisé la identidad/ }))
  expect(form.getByRole('button', { name: 'Crear producto local' })).toBeDisabled()
  fireEvent.change(form.getByRole('textbox', { name: 'Principio activo 1' }), { target: { value: 'Ingrediente sintético de prueba' } })
  fireEvent.change(form.getByRole('textbox', { name: 'Concentración documentada 1' }), { target: { value: '25 µg' } })
  fireEvent.change(form.getByRole('textbox', { name: 'Forma farmacéutica' }), { target: { value: 'Forma de prueba' } })
  fireEvent.click(form.getByRole('checkbox', { name: /Revisé la identidad/ }))
  fireEvent.click(form.getByRole('button', { name: 'Crear producto local' }))
  await screen.findByText(/Operación guardada y producto consultado/)
  expect(fake.writes()[0]?.[1]?.body).toContain('"components":[{"ingredient":"Ingrediente sintético de prueba","strengthNormalized":"25 µg"}]')
})
it('shows an empty global catalog honestly and associates a selected reference without inventory writes', async () => {
  const fake = server(); mount(fake)
  fireEvent.click(screen.getByRole('button', { name: 'Catálogo global' }))
  await screen.findByText('No hay productos globales en esta página.')
  fake.state.globals = [{ ...record, id: id('90'), name: 'Referencia unitaria' }]
  fireEvent.click(screen.getAllByRole('button', { name: 'Actualizar lista' })[0]!)
  fireEvent.click(await screen.findByRole('button', { name: 'Seleccionar referencia Referencia unitaria' }))
  const form = within(screen.getByRole('form', { name: 'Asociar producto global' }))
  fireEvent.change(form.getByRole('textbox', { name: 'Código interno' }), { target: { value: 'GLOBAL-UNIT' } })
  fireEvent.change(form.getByRole('textbox', { name: 'Precio minorista por unidad base (S/)' }), { target: { value: '2.00' } })
  fireEvent.click(form.getByRole('checkbox', { name: /Revisé la identidad/ }))
  fireEvent.click(form.getByRole('button', { name: 'Asociar al negocio' }))
  await screen.findByText(/Operación guardada y producto consultado/)
  expect(fake.writes()).toHaveLength(1)
  expect(String(fake.writes()[0]?.[0])).toMatch(/\/products\/from-global$/)
  expect(fake.state.products[1]?.globalProductId).toBe(id('90'))
})
it('retains uncertain creation across reload and never resends it automatically', async () => {
  const fake = server(); fake.state.loseCreate = true; const view = mount(fake); const form = await fillLocal()
  fireEvent.click(form.getByRole('checkbox', { name: /Revisé la identidad/ }))
  fireEvent.click(form.getByRole('button', { name: 'Crear producto local' }))
  await screen.findByText('No pudimos completar la operación de catálogo')
  const search = screen.getByTestId('location').textContent!
  expect(search).toContain('catalogPending=create')
  view.unmount(); mount(fake, search)
  expect(screen.getByRole('button', { name: 'Nuevo producto local' })).toBeDisabled()
  await screen.findByRole('button', { name: 'Ver Nuevo producto unitario' })
  expect(fake.writes()).toHaveLength(1)
})
it('updates prices and status through their official endpoints, refetching actual detail', async () => {
  const fake = server(); mount(fake); await selectProduct()
  const form = within(screen.getByRole('form', { name: 'Actualizar precios' }))
  fireEvent.change(form.getByRole('textbox', { name: 'Precio minorista por unidad base (S/)' }), { target: { value: '2.1234' } })
  fireEvent.click(form.getByRole('checkbox', { name: 'Confirmo los nuevos precios base.' }))
  fireEvent.click(form.getByRole('button', { name: 'Guardar precios' }))
  await screen.findByText(/Operación guardada y producto consultado/)
  await waitFor(() => expect(screen.getByRole('textbox', { name: 'Precio minorista por unidad base (S/)' })).toHaveValue('2.1234'))
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirmo desactivar este producto.' }))
  fireEvent.click(screen.getByRole('button', { name: 'Desactivar producto' }))
  await screen.findByRole('button', { name: 'Activar producto' })
  expect(fake.writes()).toHaveLength(2)
  expect(String(fake.writes()[1]?.[0])).toMatch(/\/status$/)
  expect(fake.state.products[0]?.isActive).toBe(false)
})
it('does not retry uncertain prices or unlock after an early read without operator review', async () => {
  const fake = server(); fake.state.losePrices = true; mount(fake); await selectProduct()
  fireEvent.change(screen.getByRole('textbox', { name: 'Precio minorista por unidad base (S/)' }), { target: { value: '2.00' } })
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirmo los nuevos precios base.' }))
  fireEvent.click(screen.getByRole('button', { name: 'Guardar precios' }))
  await screen.findByText(/Producto consultado: Producto unitario/)
  expect(screen.getByRole('button', { name: 'Guardar precios' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Continuar tras conciliación verificada' })).toBeDisabled()
  expect(fake.writes()).toHaveLength(1)
  fireEvent.change(screen.getByRole('textbox', { name: 'Código interno de la solicitud original' }), { target: { value: 'OTRO-CODIGO' } })
  fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué el código/ }))
  expect(screen.getByRole('button', { name: 'Continuar tras conciliación verificada' })).toBeDisabled()
  fireEvent.change(screen.getByRole('textbox', { name: 'Código interno de la solicitud original' }), { target: { value: 'UNIT' } })
  fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué el código/ }))
  fireEvent.click(screen.getByRole('button', { name: 'Continuar tras conciliación verificada' }))
  await screen.findByText(/El operador revisó el estado consultado/)
  expect(fake.writes()).toHaveLength(1)
})
it('validates a proposed initial configuration but blocks its destructive endpoint without an atomic contract', async () => {
  const fake = server(); fake.state.units = []; mount(fake); await selectProduct()
  const form = within(screen.getByRole('form', { name: 'Configurar presentaciones iniciales' }))
  fireEvent.change(form.getByRole('textbox', { name: 'Nombre de presentación 1' }), { target: { value: 'Unidad' } })
  fireEvent.click(form.getByRole('button', { name: 'Agregar presentación' }))
  fireEvent.change(form.getByRole('textbox', { name: 'Nombre de presentación 2' }), { target: { value: 'Caja' } })
  fireEvent.change(form.getByRole('textbox', { name: 'Conversión a base 2' }), { target: { value: '10' } })
  fireEvent.click(form.getByRole('checkbox', { name: /Revisé el conjunto completo/ }))
  expect(form.getByRole('button', { name: 'Guardar presentaciones iniciales (bloqueado por contrato)' })).toBeDisabled()
  expect(form.getByRole('textbox', { name: 'Conversión a base 2' })).toHaveValue('10')
  expect(fake.writes()).toHaveLength(0)
  expect(fake.state.units).toHaveLength(0)
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
  fireEvent.click(screen.getByRole('button', { name: 'Nuevo producto local' }))
  await screen.findByText('No pudimos consultar las categorías')
  expect(screen.queryByRole('form', { name: 'Crear producto local' })).not.toBeInTheDocument()
  expect(fake.writes()).toHaveLength(0)
})
it('handles a definitive authorization rejection without changing status or retrying', async () => {
  const fake = server(); fake.state.rejectStatus = true; mount(fake); await selectProduct()
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirmo desactivar este producto.' }))
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
  view.changeContext(next)
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
  mount(fake); fireEvent.click(await screen.findByRole('button', { name: 'Ver Producto unitario' }))
  await waitFor(() => expect(signal).toBeDefined())
  fireEvent.click(screen.getByRole('button', { name: 'Ver Otro producto unitario' }))
  await screen.findByRole('heading', { name: 'Otro producto unitario' })
  expect(signal?.aborted).toBe(true)
  await act(async () => release())
  expect(within(screen.getByRole('region', { name: 'Detalle del producto' })).queryByRole('heading', { name: 'Producto unitario' })).not.toBeInTheDocument()
})
