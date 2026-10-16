import { expect, it } from 'vitest'
import { HttpClient } from '../../shared/api/http'
import { transfersApi } from './api'
import { id, n, transfer, transferServer, workspace, json } from './transfers.test.support'

it('uses the official paths, branch/state pagination and exact numeric bodies with same-origin CSRF', async () => {
  const fake = transferServer(); fake.state.records = [{ ...transfer, events: [...transfer.events] }]
  const api = transfersApi(new HttpClient(fake.fetcher))
  await api.list(workspace, workspace.branchId, 'requested', 20)
  expect(String(fake.fetcher.mock.calls[0]?.[0])).toContain(`/transfers?offset=20&limit=20&branchId=${workspace.branchId}&status=requested`)
  await api.read(workspace, transfer.id)
  await api.create(workspace, transfer.sourceBranchId, transfer.destinationBranchId, [{ businessProductId: id(10), productUnitId: id(11), quantity: n('0.000000000001') }])
  const write = fake.writes()[0]![1]!
  expect(write.body).toContain('"quantity":0.000000000001'); expect(write.credentials).toBe('same-origin')
  expect(new Headers(write.headers).get('X-CSRF-TOKEN')).toBe('transfers-unit-csrf')
  expect(String(fake.writes()[0]![0])).toBe(`/api/tenants/${workspace.tenantId}/transfers`)
  await api.approve(workspace, transfer.id)
  await api.cancel(workspace, transfer.id, 'Motivo sintético')
  expect(fake.writes()[1]?.[1]?.body).toBe('{}'); expect(fake.writes()[2]?.[1]?.body).toBe('{"reason":"Motivo sintético"}')
})
it('rejects invalid pages, unknown enum values, foreign scopes, mismatched IDs and malformed details', async () => {
  const fake = transferServer(); fake.state.records = [transfer]; const api = transfersApi(new HttpClient(fake.fetcher))
  for (const offset of [-1, 10001, 0.5]) await expect(api.list(workspace, undefined, undefined, offset)).rejects.toThrow()
  fake.state.records = [{ ...transfer, tenantId: id(999) }]; await expect(api.read(workspace, transfer.id)).rejects.toMatchObject({ code: 'client.unexpected_response' })
  fake.fetcher.mockResolvedValueOnce(json({ ...transfer, id: id(999) })); await expect(api.read(workspace, transfer.id)).rejects.toMatchObject({ code: 'client.unexpected_response' })
  fake.fetcher.mockResolvedValueOnce(json({ ...transfer, status: 'invented' })); await expect(api.read(workspace, transfer.id)).rejects.toThrow()
  fake.fetcher.mockResolvedValueOnce(json({ ...transfer, lines: [] })); await expect(api.read(workspace, transfer.id)).rejects.toThrow()
  fake.fetcher.mockResolvedValueOnce(json([{ ...transfer, sourceBranchId: id(77), destinationBranchId: id(78) }])); await expect(api.list(workspace, workspace.branchId, undefined)).rejects.toThrow()
})
it('marks malformed successful mutation results unknown instead of claiming failure or fabricating a record', async () => {
  const fake = transferServer(); fake.state.malformed = true; const api = transfersApi(new HttpClient(fake.fetcher))
  await expect(api.create(workspace, transfer.sourceBranchId, transfer.destinationBranchId, [{ businessProductId: id(10), productUnitId: id(11), quantity: n('1') }])).rejects.toMatchObject({ outcomeUnknown: true })
  expect(fake.writes()).toHaveLength(1)
})
