import { render } from '@testing-library/react'
import { vi } from 'vitest'
import type { ReactNode } from 'react'
import { MemoryRouter, useLocation } from 'react-router'
import { RuntimeProvider } from '../../app/providers/runtime'
import { WorkspaceContext, createMediPosService } from '../access'
import type { OperationalAccess, Workspace } from '../access'
import { HttpClient } from '../../shared/api/http'
import { multiply } from '../../shared/api/decimal'
import { parseExactJson } from '../../shared/api/exactJson'
import { id, n, time, workspace, detail, lot, json } from '../../test/operations'
import type { DispatchSelection, ReceiptSelection, Transfer, TransferLineInput } from './api'

// Deliberately synthetic HTTP transport only; these are not PostgreSQL or OIDC tests.
export { id, n, time, workspace, detail, lot, json }
export const other = { ...workspace.branch, id: id(6), name: 'Origen unitario' }
export const transfer: Transfer = { id: id(60), tenantId: workspace.tenantId, sourceBranchId: other.id, sourceBranchName: other.name, destinationBranchId: workspace.branchId, destinationBranchName: workspace.branch.name, status: 'requested', requestedAt: time, updatedAt: time,
  lines: [{ id: id(61), businessProductId: detail.product.id, productUnitIdSnapshot: detail.units[0]!.id, requestedQuantity: n('2'), requestedBaseQuantity: n('2'), unitNameSnapshot: 'Unidad', conversionToBaseSnapshot: n('1') }],
  events: [{ id: id(62), eventType: 'requested', actorId: workspace.session.userId, occurredAt: time, reason: null }], allocations: [] }
export function allowed(branchId: string, role = workspace.access.role): OperationalAccess { return { isAllowed: true, code: 'access.allowed', context: { ...workspace.access, branchId, role } } }
export function transferServer() {
  const state = { records: [] as Transfer[], branches: [workspace.branch, other], access: new Map([[workspace.branchId, allowed(workspace.branchId)], [other.id, allowed(other.id)]]),
    lots: [lot], failList: false, failRead: false, reject: 0, lose: '', hold: false, malformed: false,
    delayRead: null as Promise<Response> | null }
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (path, init) => {
    const url = new URL(String(path), 'https://unit.test'); const method = init?.method ?? 'GET'
    if (url.pathname === '/auth/csrf') return json({ requestToken: 'transfers-unit-csrf' })
    if (url.pathname.endsWith('/branches')) return json(state.branches)
    if (url.pathname.endsWith('/access')) return json(state.access.get(url.searchParams.get('branchId')!) ?? { isAllowed: false, code: 'access.branch_not_assigned', context: null })
    if (url.pathname.endsWith('/products/search')) return json({ directMatches: [{ ...detail.product, businessProductId: detail.product.id, quantityAvailableBase: n('0'), units: detail.units }], equivalentInBranch: [], otherBranchAvailability: [] })
    if (url.pathname.endsWith('/products/' + detail.product.id)) return json(detail)
    if (url.pathname.endsWith('/inventory/lots')) return json(state.lots.slice(Number(url.searchParams.get('offset') ?? '0'), Number(url.searchParams.get('offset') ?? '0') + 20))
    if (method === 'GET' && url.pathname.endsWith('/transfers')) {
      if (state.failList) return json({ code: 'unit.unavailable' }, 503)
      const branch = url.searchParams.get('branchId'); const status = url.searchParams.get('status'); const offset = Number(url.searchParams.get('offset') ?? '0')
      return json(state.records.filter(row => (!branch || row.sourceBranchId === branch || row.destinationBranchId === branch) && (!status || row.status === status)).slice(offset, offset + 20))
    }
    const transferId = url.pathname.match(/\/transfers\/([^/]+)/)?.[1]
    if (method === 'GET' && transferId) return state.delayRead ?? (state.failRead ? json({ code: 'unit.unavailable' }, 503) : state.records.find(row => row.id === transferId) ? json(state.records.find(row => row.id === transferId)) : json({ code: 'transfer.not_found' }, 404))
    if (method === 'POST' && url.pathname.includes('/transfers')) {
      if (state.reject) return json({ code: 'unit.denied' }, state.reject)
      const action = transferId ? url.pathname.split('/').at(-1)! : 'create'
      const data = parseExactJson(String(init?.body)); let result = state.records.find(row => row.id === transferId)
      if (!transferId) {
        const body = data as { sourceBranchId: string; destinationBranchId: string; lines: TransferLineInput[] }
        result = { ...transfer, sourceBranchId: body.sourceBranchId, destinationBranchId: body.destinationBranchId, lines: body.lines.map((row, index) => { const unit = detail.units.find(unit => unit.id === row.productUnitId)!; return { id: id(61 + index), businessProductId: row.businessProductId, productUnitIdSnapshot: row.productUnitId, requestedQuantity: row.quantity, requestedBaseQuantity: multiply(row.quantity, unit.conversionToBase), unitNameSnapshot: unit.name, conversionToBaseSnapshot: unit.conversionToBase } }) }; state.records.push(result)
      } else if (result && !state.hold) {
        if (action === 'approve') result.status = 'approved'
        if (action === 'cancel') result.status = 'cancelled'
        if (action === 'dispatch') {
          const body = data as { allocations: DispatchSelection[] }; result.status = 'in_transit'; result.allocations = body.allocations.map((row, index) => ({ id: id(80 + index), transferLineId: row.transferLineId, sourceInventoryLotId: row.inventoryLotId, sourcePurchaseLineId: id(90 + index), businessProductId: detail.product.id, batchNumber: state.lots.find(lot => lot.inventoryLotId === row.inventoryLotId)!.batchNumber, expirationDate: lot.expirationDate, dispatchedQuantityBase: row.quantityBase, receivedQuantityBase: null, differenceBase: null }))
        }
        if (action === 'receive') {
          const body = data as { quantities: ReceiptSelection[] }; result.status = 'received'; result.allocations = result.allocations.map(row => ({ ...row, receivedQuantityBase: body.quantities.find(q => q.allocationId === row.id)!.quantityBase, differenceBase: n('0') }))
        }
        result.events = [...result.events, { id: id(100 + result.events.length), eventType: action === 'dispatch' ? 'dispatched' : action === 'receive' ? 'received' : action === 'approve' ? 'approved' : 'cancelled', actorId: workspace.session.userId, occurredAt: time, reason: action === 'cancel' ? (data as { reason: string }).reason : null }]
      }
      if (state.lose === action) throw new TypeError('Synthetic response loss')
      return json(state.malformed ? {} : result, action === 'create' ? 201 : 200)
    }
    throw new Error('Unexpected synthetic transfer route: ' + method + ' ' + url.pathname)
  })
  return { state, fetcher, writes: () => fetcher.mock.calls.filter(([, init]) => init?.method === 'POST') }
}
export function mountTransfers(fake: ReturnType<typeof transferServer>, page: ReactNode, search = '', context = workspace) {
  function Location() { return <output data-testid="location">{useLocation().search}</output> }
  const http = new HttpClient(fake.fetcher); const runtime = { http, service: createMediPosService(http), preview: false }
  const tree = (w: Workspace) => <RuntimeProvider runtime={runtime}><MemoryRouter initialEntries={['/transfers' + search]}><WorkspaceContext.Provider value={w}>{page}<Location /></WorkspaceContext.Provider></MemoryRouter></RuntimeProvider>
  const view = render(tree(context)); return { ...view, changeContext: (w: Workspace) => view.rerender(tree(w)) }
}
