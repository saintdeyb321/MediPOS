import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { RuntimeProvider } from '../../app/providers/runtime'
import { WorkspaceContext } from '../access'
import type { Workspace } from '../access'
import { createPreviewService } from '../../development/fixtures'
import { HttpClient } from '../../shared/api/http'
import { JsonNumber, stringifyExactJson } from '../../shared/api/exactJson'
import type { ProductMatch, ProductSearch } from '../catalog'
import { ProductSearchPanel } from './ProductSearchPanel'

// Component-level transport doubles; never evidence of PostgreSQL or OIDC integration.
const tenantId = '019a0162-0000-7000-8000-000000000002'
const branchId = '019a0162-0000-7000-8000-000000000005'
const otherBranchId = '019a0162-0000-7000-8000-000000000006'
const product: ProductMatch = {
  businessProductId: '019a0162-0000-7000-8000-000000000011', name: 'Producto de contrato', productType: 'retail',
  brandOrLaboratory: 'Laboratorio de contrato', internalCode: 'TEST', barcode: 'TEST-BARCODE',
  retailPrice: new JsonNumber('99999999999999.9999'), wholesalePrice: null, quantityAvailableBase: new JsonNumber('5.0000'),
  units: [
    { id: '019a0162-0000-7000-8000-000000000012', name: 'Unidad', conversionToBase: new JsonNumber('1'), isBaseUnit: true, isActive: true },
    { id: '019a0162-0000-7000-8000-000000000013', name: 'Caja', conversionToBase: new JsonNumber('10'), isBaseUnit: false, isActive: true },
  ],
}
const result = (matches: ProductMatch[] = []): ProductSearch => ({ directMatches: matches, equivalentInBranch: [], otherBranchAvailability: [] })
const json = (data: ProductSearch) => new Response(stringifyExactJson(data), { headers: { 'Content-Type': 'application/json' } })
function workspace(branch = branchId): Workspace {
  return { tenantId, branchId: branch, tenantName: 'Contrato unitario', base: `/boticas/${tenantId}/sucursales/${branch}`,
    branch: { id: branch, tenantId, legalEntityId: tenantId, name: 'Sucursal de contrato', isMainHub: false, createdAt: '2026-10-15T00:00:00Z' },
    session: { userId: '019a0162-0000-7000-8000-000000000001' },
    access: { userId: '019a0162-0000-7000-8000-000000000001', tenantId, branchId: branch, membershipId: tenantId, role: 'owner',
      membershipIsActive: true, branchAssigned: true, withinSchedule: true, licenseStatus: 'trial', licenseStartsAt: null, licenseExpiresAt: null, evaluatedAtUtc: '2026-10-15T00:00:00Z' } }
}
function mount(fetcher: typeof fetch) {
  const runtime = { service: createPreviewService('owner'), http: new HttpClient(fetcher), preview: false }
  const selected = vi.fn()
  const tree = (branch = branchId) => <RuntimeProvider runtime={runtime}><MemoryRouter><WorkspaceContext.Provider value={workspace(branch)}><ProductSearchPanel key={branch} onSelect={selected} /></WorkspaceContext.Provider></MemoryRouter></RuntimeProvider>
  const view = render(tree())
  return { ...view, selected, changeBranch: () => view.rerender(tree(otherBranchId)) }
}
const search = (text: string) => fireEvent.change(screen.getByRole('searchbox', { name: 'Buscar un producto' }), { target: { value: text } })
afterEach(() => vi.useRealTimers())

describe('real-client POS search component', () => {
  it('debounces 300ms, skips blank input and uses only the scoped search endpoint', async () => {
    vi.useFakeTimers()
    const fetcher = vi.fn<typeof fetch>().mockImplementation(async () => json(result()))
    const view = mount(fetcher)
    search('   ')
    await act(() => vi.advanceTimersByTimeAsync(400))
    expect(fetcher).not.toHaveBeenCalled()
    search('  ácido & código  ')
    await act(() => vi.advanceTimersByTimeAsync(299))
    expect(fetcher).not.toHaveBeenCalled()
    await act(() => vi.advanceTimersByTimeAsync(1))
    expect(fetcher).toHaveBeenCalledOnce()
    expect(fetcher.mock.calls[0]?.[0]).toBe(`/api/tenants/${tenantId}/branches/${branchId}/products/search?q=%C3%A1cido+%26+c%C3%B3digo&limit=20`)
    expect(fetcher.mock.calls[0]?.[1]).toMatchObject({ credentials: 'same-origin', method: 'GET' })
    view.unmount()
  })
  it('shows the actual empty result after loading and never inserts example products', async () => {
    const fetcher = vi.fn<typeof fetch>().mockImplementation(async () => json(result()))
    mount(fetcher)
    expect(screen.getByRole('heading', { name: 'Busca productos de tu botica' })).toBeInTheDocument()
    search('sin coincidencia')
    expect(screen.getByRole('status')).toHaveTextContent('Buscando productos')
    expect(await screen.findByRole('heading', { name: 'Sin coincidencias en esta sucursal' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Seleccionar / })).not.toBeInTheDocument()
  })
  it('emits an exact selection without writes; subsequent searching does not clear it', async () => {
    const fetcher = vi.fn<typeof fetch>().mockImplementation(async () => json(result([product])))
    const view = mount(fetcher); search('TEST')
    const select = await screen.findByRole('button', { name: `Seleccionar ${product.name}` })
    expect(screen.getByText('S/ 99999999999999.9999')).toBeInTheDocument()
    expect(screen.getByText('5', { selector: 'dd' })).toBeInTheDocument()
    expect(screen.queryByText('5.0000')).not.toBeInTheDocument()
    fireEvent.change(screen.getByRole('combobox', { name: /Presentación de/ }), { target: { value: product.units[1]!.id } })
    expect(select).toBeDisabled()
    expect(select).toHaveAccessibleDescription(/No hay stock vendible suficiente/)
    fireEvent.change(screen.getByRole('combobox'), { target: { value: product.units[0]!.id } })
    fireEvent.click(select)
    expect(view.selected).toHaveBeenCalledExactlyOnceWith({ product, unit: product.units[0], quantity: new JsonNumber('1') })
    expect(fetcher.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true)
    search('otra consulta')
    expect(view.selected).toHaveBeenCalledOnce()
  })
  it('does not offer zero-stock or inactive presentations as available', async () => {
    const fetcher = vi.fn<typeof fetch>().mockImplementation(async () => json(result([{ ...product, quantityAvailableBase: new JsonNumber('0'), units: product.units.map(unit => ({ ...unit, isActive: false })) }])))
    mount(fetcher); search('TEST')
    expect(await screen.findByRole('button', { name: `Seleccionar ${product.name}` })).toBeDisabled()
    expect(screen.getByText('Sin presentación vendible')).toBeInTheDocument()
    expect(screen.queryByText('Stock disponible')).not.toBeInTheDocument()
    expect(screen.getByRole('combobox')).toBeDisabled()
  })
  it('shows sanitized denial, without an empty-success state or automatic retries', async () => {
    const fetcher = vi.fn<typeof fetch>().mockImplementation(async () => new Response(JSON.stringify({ code: 'access.license_denied', detail: 'PRIVATE detail' }), { status: 403, headers: { 'Content-Type': 'application/problem+json' } }))
    mount(fetcher); search('TEST')
    expect(await screen.findByRole('alert')).toHaveTextContent('licencia')
    expect(screen.queryByText(/PRIVATE/)).not.toBeInTheDocument()
    expect(screen.queryByText('Sin coincidencias en esta sucursal')).not.toBeInTheDocument()
    expect(fetcher).toHaveBeenCalledOnce()
  })
  it('offers an explicit read retry after server failure', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValueOnce(new Response(JSON.stringify({ code: 'server.failure' }), { status: 500, headers: { 'Content-Type': 'application/problem+json' } })).mockImplementation(async () => json(result()))
    mount(fetcher); search('TEST')
    fireEvent.click(await screen.findByRole('button', { name: 'Volver a intentar' }))
    expect(await screen.findByRole('heading', { name: 'Sin coincidencias en esta sucursal' })).toBeInTheDocument()
    expect(fetcher).toHaveBeenCalledTimes(2)
  })
  it.each(['clear', 'branch', 'unmount'] as const)('aborts the pending request on %s', async action => {
    let signal: AbortSignal | null | undefined
    const fetcher = vi.fn<typeof fetch>().mockImplementation((_, init) => new Promise<Response>((_, reject) => {
      signal = init?.signal
      signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true })
    }))
    const view = mount(fetcher); search('TEST')
    await waitFor(() => expect(fetcher).toHaveBeenCalledOnce())
    if (action === 'clear') search('')
    if (action === 'branch') view.changeBranch()
    if (action === 'unmount') view.unmount()
    await waitFor(() => expect(signal?.aborted).toBe(true))
    if (action !== 'unmount') expect(screen.getByRole('searchbox')).toHaveValue('')
  })
  it('aborts an older query and ignores its late response', async () => {
    let firstSignal: AbortSignal | null | undefined
    let finish!: (response: Response) => void
    const newer = { ...product, name: 'Resultado nuevo' }
    const fetcher = vi.fn<typeof fetch>().mockImplementationOnce((_, init) => {
      firstSignal = init?.signal
      return new Promise<Response>(resolve => { finish = resolve })
    }).mockImplementation(async () => json(result([newer])))
    mount(fetcher); search('anterior')
    await waitFor(() => expect(fetcher).toHaveBeenCalledOnce())
    search('nueva')
    await waitFor(() => expect(firstSignal?.aborted).toBe(true))
    expect(await screen.findByRole('button', { name: 'Seleccionar Resultado nuevo' })).toBeInTheDocument()
    await act(async () => finish(json(result([product]))))
    expect(screen.queryByRole('button', { name: `Seleccionar ${product.name}` })).not.toBeInTheDocument()
  })
})
