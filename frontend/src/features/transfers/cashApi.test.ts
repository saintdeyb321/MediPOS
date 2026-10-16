import { expect, it } from 'vitest'
import { HttpClient } from '../../shared/api/http'
import { cashSettled, cashTransfersApi } from './cashApi'
import { cashServer, sentCash } from './cash.test.support'
import { id, n, workspace, json } from './transfers.test.support'

it('uses cash endpoints and exact PEN literals, obtains CSRF and rereads scoped records', async () => {
  const fake = cashServer(); const api = cashTransfersApi(new HttpClient(fake.fetcher)); fake.cash.records = [sentCash]
  await api.pending(workspace, workspace.branchId, 20)
  expect(String(fake.fetcher.mock.calls[0]?.[0])).toContain(`/cash-transfers/pending?offset=20&limit=20&destinationBranchId=${workspace.branchId}`)
  await api.read(workspace, sentCash.id)
  const sent = await api.dispatch(workspace, id(302), id(6), n('99999999999999.9999'))
  expect(sent.amount.literal).toBe('99999999999999.9999'); expect(String(fake.writes()[0]?.[0])).toContain(`/branches/${workspace.branchId}/cash-transfers`)
  expect(fake.writes()[0]?.[1]?.body).toContain('"amount":99999999999999.9999'); expect(new Headers(fake.writes()[0]?.[1]?.headers).get('X-CSRF-TOKEN')).toBe('transfers-unit-csrf')
  fake.cash.records = [sentCash]; const received = await api.receive(workspace, sentCash.id, id(302))
  expect(fake.writes()[1]?.[1]?.body).toBe(`{"destinationCashSessionId":"${id(302)}"}`); expect(cashSettled(received, 'receive')).toBe(true); expect(cashSettled(sentCash, 'receive')).toBe(false)
})
it('rejects corrupt state/tenant/amount/receipt and marks successful mismatched writes unknown', async () => {
  const fake = cashServer(); const api = cashTransfersApi(new HttpClient(fake.fetcher))
  for (const changed of [{ tenantId: id(999) }, { id: id(999) }, { amount: n('0') }, { amount: n('0.00001') }, { amount: n('100000000000000') }, { status: 'received' }, { destinationCashSessionId: id(302) }]) {
    fake.fetcher.mockResolvedValueOnce(json({ ...sentCash, ...changed })); await expect(api.read(workspace, sentCash.id)).rejects.toThrow()
  }
  fake.cash.records = [sentCash]; fake.fetcher.mockResolvedValueOnce(json({ items: [sentCash], hasMore: false })); await expect(api.pending(workspace, id(6))).rejects.toThrow()
  const original = fake.fetcher.getMockImplementation()!; fake.fetcher.mockImplementation(async (path, init) => init?.method === 'POST' ? json({ ...sentCash, sourceBranchId: id(999) }) : original(path, init))
  await expect(api.dispatch(workspace, id(302), id(6), n('1'))).rejects.toMatchObject({ outcomeUnknown: true })
})
