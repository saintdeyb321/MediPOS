// @vitest-environment jsdom
import { expect, it, vi } from 'vitest'
import { HttpClient } from './http'
import { stringifyExactJson } from './exactJson'
import { recoverOperation } from './operations'
import { inventoryApi } from '../../features/inventory/api'
import { id, json, n, workspace } from '../../test/operations'

it.each(['operation.in_progress', 'operation.intention_conflict'])('retains uncertainty for %s and never retries the write', async code => {
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => String(path) === '/auth/csrf' ? json({ requestToken: 'test-csrf' }) : json({ code }, 409))
  await expect(new HttpClient(fetcher).request(`/api/tenants/${workspace.tenantId}/suppliers`, { method: 'POST', body: { name: 'Synthetic' }, intention: { id: id(80), branchId: workspace.branchId } })).rejects.toMatchObject({ status: 409, code, outcomeUnknown: true })
  expect(fetcher.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1)
})
it('queries the exact scoped intention and preserves decimal literals from its original adjustment', async () => {
  const result = { inventoryLotId: id(30), stockMovementId: id(31), before: n('9007199254740993.123456789012'), after: n('9007199254740992.123456789012') }
  const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json({ requestId: id(80), kind: 'adjustment', resultJson: stringifyExactJson(result) }))
  const observed = await inventoryApi(new HttpClient(fetcher)).recover(workspace, id(80), 'adjustment')
  expect('before' in observed && observed.before.literal).toBe(result.before.literal)
  expect(fetcher.mock.calls[0]?.[0]).toBe(`/api/tenants/${workspace.tenantId}/branches/${workspace.branchId}/operations/${id(80)}`)
  expect(fetcher.mock.calls).toHaveLength(1)
})
it('does not accept another intention or kind and does not interpret an unobserved receipt as rollback', async () => {
  const wrong = vi.fn<typeof fetch>().mockResolvedValue(json({ requestId: id(81), kind: 'supplier', resultJson: '{}' }))
  await expect(recoverOperation(new HttpClient(wrong), workspace, id(80), 'supplier')).rejects.toMatchObject({ code: 'client.unexpected_response' })
  const missing = vi.fn<typeof fetch>().mockResolvedValue(json({ code: 'operation.not_observed' }, 404))
  await expect(recoverOperation(new HttpClient(missing), workspace, id(80), 'supplier')).rejects.toMatchObject({ status: 404, code: 'operation.not_observed' })
  expect(missing.mock.calls).toHaveLength(1)
})
