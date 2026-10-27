// @vitest-environment jsdom
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { ProductSearchPanel } from './ProductSearchPanel'
import { catalogApi } from '../catalog/api'
import { addSelection } from './cart'
import { id, json, mountOperation, n, operationServer, workspace, detail } from '../../test/operations'
import { HttpClient } from '../../shared/api/http'

it('separates structured local matches from other-branch stock and offers no remote-stock add action', async () => {
  const fake = operationServer(); const normal = fake.fetcher.getMockImplementation()!
  const direct = { ...detail.product, businessProductId: id(10), quantityAvailableBase: n('2'), units: detail.units, wholesalePrice: n('0.80') }
  const equivalent = { ...direct, businessProductId: id(81), name: 'Otro producto estructurado', quantityAvailableBase: n('3') }
  fake.fetcher.mockImplementation(async (path, init) => {
    if (String(path).includes('/operational-')) return json([])
    if (String(path).includes('/products/search?')) return json({ directMatches: [direct], equivalentInBranch: [{ product: equivalent, forDirectProducts: [id(10)], description: 'Coincidencia de composición y forma documentada' }], otherBranchAvailability: [{ branchId: id(90), branchName: 'Otra sede unitaria', businessProductId: id(10), quantityAvailableBase: n('10') }] })
    return normal(path, init)
  })
  const select = vi.fn(); mountOperation(fake, <ProductSearchPanel onSelect={select} />)
  fireEvent.change(screen.getByRole('searchbox', { name: 'Buscar producto' }), { target: { value: 'paracetamol' } })
  const remote = await screen.findByRole('region', { name: 'Disponibilidad en otras sucursales' })
  expect(within(remote).queryByRole('button')).not.toBeInTheDocument(); expect(remote).toHaveTextContent('Otra sede unitaria')
  fireEvent.click(screen.getByRole('button', { name: 'Elegir presentación de Otro producto estructurado' }))
  fireEvent.click(screen.getByRole('button', { name: 'Seleccionar Otro producto estructurado' }))
  expect(select).toHaveBeenCalledTimes(1); expect(select.mock.calls[0]?.[0].product.businessProductId).toBe(id(81))
  expect(fake.writes()).toHaveLength(0)
})
it('selects wholesale explicitly and preserves its exact configured base price in the cart', async () => {
  const fake = operationServer(); const normal = fake.fetcher.getMockImplementation()!
  const product = { ...detail.product, businessProductId: id(10), quantityAvailableBase: n('20'), units: detail.units, wholesalePrice: n('0.1234') }
  fake.fetcher.mockImplementation(async (path, init) => String(path).includes('/operational-') ? json([]) : String(path).includes('/products/search?') ? json({ directMatches: [product], equivalentInBranch: [], otherBranchAvailability: [] }) : normal(path, init))
  const select = vi.fn(); mountOperation(fake, <ProductSearchPanel onSelect={select} />)
  fireEvent.change(screen.getByRole('searchbox', { name: 'Buscar producto' }), { target: { value: 'unidad' } })
  fireEvent.click(await screen.findByRole('button', { name: 'Elegir presentación' }))
  fireEvent.change(screen.getByRole('combobox', { name: 'Tipo de precio' }), { target: { value: 'wholesale' } })
  fireEvent.click(screen.getByRole('button', { name: 'Seleccionar Producto sintético unitario' }))
  const lines = addSelection([], select.mock.calls[0]![0]); expect(lines[0]?.priceKind).toBe('wholesale'); expect(lines[0]?.basePrice.literal).toBe('0.1234')
  expect(fake.writes()).toHaveLength(0)
})
it('uses server category/query pagination under the operational branch route and rejects a foreign tenant response', async () => {
  const fake = vi.fn<typeof fetch>().mockResolvedValue(json([detail.product])); const api = catalogApi(new HttpClient(fake))
  const rows = await api.operationalList(workspace, 'producto', detail.product.categoryId, 50, 50)
  expect(rows[0]?.id).toBe(detail.product.id)
  const path = String(fake.mock.calls[0]?.[0]); expect(path).toContain('/operational-products?'); expect(path).toContain('offset=50&limit=50'); expect(path).toContain('categoryId=' + detail.product.categoryId); expect(path).toContain('q=producto')
  fake.mockResolvedValue(json([{ ...detail.product, tenantId: id(99) }]))
  await expect(api.operationalList(workspace, '', '', 0, 50)).rejects.toMatchObject({ code: 'client.unexpected_response' })
  await waitFor(() => expect(fake).toHaveBeenCalledTimes(2))
})

it('rejects a wholesale selection when the product has no configured wholesale price', () => {
  const product = { ...detail.product, businessProductId: id(10), quantityAvailableBase: n('20'), units: detail.units }
  expect(() => addSelection([], { product, unit: detail.units[0]!, quantity: n('1'), priceKind: 'wholesale' })).toThrow('precio mayorista')
})
