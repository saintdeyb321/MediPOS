import { parseExactJson } from '../../shared/api/exactJson'
import { transferServer, workspace, other, id, n, time, json } from './transfers.test.support'
import type { CashTransfer } from './cashApi'

export const sentCash: CashTransfer = { id: id(300), tenantId: workspace.tenantId, sourceBranchId: other.id, sourceCashSessionId: id(301), destinationBranchId: workspace.branchId, destinationCashSessionId: null, amount: n('1.0001'), status: 'in_transit', dispatchedAt: time, dispatchedByActorId: workspace.session.userId, receivedAt: null, receivedByActorId: null }
export function cashServer() {
  const fake = transferServer(); const base = fake.fetcher.getMockImplementation()!
  const cash = { records: [] as CashTransfer[], expected: n('10'), open: true, membershipId: workspace.access.membershipId, sessions: [id(302)], reject: 0, lose: '', hold: false, fail: false }
  fake.fetcher.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'https://unit.test'); const method = init?.method ?? 'GET'
    if (url.pathname.endsWith('/cash-sessions/active')) return json(cash.open ? cash.sessions.map(cashSessionId => ({ cashSessionId, branchId: workspace.branchId, branchName: workspace.branch.name, membershipId: cash.membershipId, userId: workspace.session.userId, displayName: 'Operador unitario', openingAmount: n('10'), openedAt: time, accumulatedSales: n('0') })) : [])
    if (url.pathname.endsWith('/cash-sessions/current')) return json({ session: cash.open ? { cashSessionId: id(302), tenantId: workspace.tenantId, branchId: workspace.branchId, membershipId: cash.membershipId, openedByActorId: workspace.session.userId, openingAmount: n('10'), openedAt: time } : null })
    if (/\/cash-sessions\/[^/]+$/.test(url.pathname)) return json({ cashSessionId: url.pathname.split('/').at(-1), branchId: workspace.branchId, membershipId: cash.membershipId, openingAmount: n('10'), openedAt: time, closedAt: cash.open ? null : time, status: cash.open ? 'open' : 'closed', expectedCashAmount: cash.expected })
    if (method === 'GET' && url.pathname.endsWith('/cash-transfers/pending')) {
      if (cash.fail) return json({ code: 'unit.unavailable' }, 503)
      const destination = url.searchParams.get('destinationBranchId'); const offset = Number(url.searchParams.get('offset') ?? '0')
      const rows = cash.records.filter(row => row.status === 'in_transit' && (!destination || row.destinationBranchId === destination))
      return json({ items: rows.slice(offset, offset + 20), hasMore: rows.length > offset + 20 })
    }
    const target = url.pathname.match(/\/cash-transfers\/([^/]+)/)?.[1]
    if (method === 'GET' && target) return cash.fail ? json({ code: 'unit.unavailable' }, 503) : cash.records.find(row => row.id === target) ? json(cash.records.find(row => row.id === target)) : json({ code: 'cash_transfer.not_found' }, 404)
    if (method === 'POST' && url.pathname.includes('/cash-transfers')) {
      if (cash.reject) return json({ code: 'unit.denied' }, cash.reject)
      const body = parseExactJson(String(init?.body)); const action = target ? 'receive' : 'create'; let result = cash.records.find(row => row.id === target)
      if (action === 'create') { result = { ...sentCash, sourceBranchId: workspace.branchId, ...body as { sourceCashSessionId: string; destinationBranchId: string; amount: CashTransfer['amount'] } }; cash.records.push(result) }
      else if (result && !cash.hold) { result = { ...result, destinationCashSessionId: (body as { destinationCashSessionId: string }).destinationCashSessionId, receivedAt: time, receivedByActorId: workspace.session.userId, status: 'received' }; cash.records = cash.records.map(row => row.id === target ? result! : row) }
      if (cash.lose === action) throw new TypeError('Synthetic cash response loss')
      return json(result, action === 'create' ? 201 : 200)
    }
    return base(input, init)
  })
  return { ...fake, cash }
}
