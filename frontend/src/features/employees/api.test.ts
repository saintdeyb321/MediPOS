import { expect, it, vi } from 'vitest'
import { HttpClient } from '../../shared/api/http'
import { employeesApi } from './api'
import { employeeServer, id, json, member, owner, worker, workspace } from './employees.test.support'

it('uses the tenant directory and bounded pagination, preserving names, roles, UUIDs and windows', async () => {
  const fake = employeeServer(); const api = employeesApi(new HttpClient(fake.fetcher))
  const result = await api.list(workspace)
  expect(result).toEqual([owner, worker]); expect(String(fake.fetcher.mock.calls[0]?.[0])).toBe(`/api/tenants/${id(2)}/employees?offset=0&limit=20`)
  await api.list(workspace, 20, 20)
  await expect(api.list(workspace, 10001)).rejects.toThrow('Invalid employee page')
  expect(fake.fetcher).toHaveBeenCalledTimes(2)
})
it('verifies snake-case enums and rejects numeric weekdays, corrupt time precision and duplicate IDs', async () => {
  for (const row of [{ ...worker, role: 1 }, { ...worker, schedule: [{ dayOfWeek: 1, startTime: '09:00:00', endTime: '18:00:00' }] }, { ...worker, schedule: [{ dayOfWeek: 'monday', startTime: '09:00:00.0000001', endTime: '18:00:00' }] }, { ...worker, branchIds: [id(5), id(5).toUpperCase()] }]) {
    const api = employeesApi(new HttpClient(vi.fn<typeof fetch>().mockResolvedValue(json([row]))))
    await expect(api.list(workspace)).rejects.toThrow('client.unexpected_response')
  }
})
it('accepts real 204 with no body and sends complete branch/window DTOs with CSRF and microseconds', async () => {
  const fake = employeeServer(); const api = employeesApi(new HttpClient(fake.fetcher))
  expect(await api.branches(workspace, worker, [id(5), id(6)], [id(5), id(6)])).toBeUndefined()
  expect(await api.schedule(workspace, worker, [{ dayOfWeek: 'monday', startTime: '09:00:00.000001', endTime: '18:00:00' }])).toBeUndefined()
  expect(fake.writes()[0]?.[1]?.body).toBe(`{"branchIds":["${id(5)}","${id(6)}"]}`)
  expect(fake.writes()[1]?.[1]?.body).toBe('{"windows":[{"dayOfWeek":"monday","startTime":"09:00:00.000001","endTime":"18:00:00"}]}')
  for (const [, init] of fake.writes()) { expect(new Headers(init?.headers).get('X-CSRF-TOKEN')).toBe('unit-csrf'); expect(init?.credentials).toBe('same-origin') }
})
it('rejects foreign branches and invalid windows without sending any write', async () => {
  const fake = employeeServer(); const api = employeesApi(new HttpClient(fake.fetcher))
  await expect(api.branches(workspace, worker, [id(99)], [id(5)])).rejects.toThrow('employees.invalid_branches')
  await expect(api.branches(workspace, worker, [id(5), id(5)], [id(5)])).rejects.toThrow('employees.invalid_branches')
  await expect(api.schedule(workspace, worker, [{ dayOfWeek: 'monday', startTime: '18:00', endTime: '09:00' }])).rejects.toThrow('employees.invalid_schedule')
  expect(fake.fetcher).not.toHaveBeenCalled()
})
it('blocks Owner deactivation regardless of apparent owner count and never dispatches its POST', async () => {
  const fake = employeeServer(); const api = employeesApi(new HttpClient(fake.fetcher))
  for (const current of [owner, { ...owner, membershipId: id(98), userId: id(99) }]) await expect(api.deactivate(workspace, current)).rejects.toThrow('employees.owner_deactivation_blocked')
  expect(fake.fetcher).not.toHaveBeenCalled()
})
it('decodes deactivation and prepared creation contracts without inventing identity discovery', async () => {
  const fake = employeeServer(); const api = employeesApi(new HttpClient(fake.fetcher))
  expect((await api.deactivate(workspace, worker)).isActive).toBe(false)
  await api.create(workspace, { userId: id(50), role: 'cashier' })
  expect(fake.writes()[1]?.[1]?.body).toBe(`{"userId":"${id(50)}","role":"cashier"}`)
  expect(fake.fetcher.mock.calls.some(([path]) => /email|invite|users/.test(String(path)))).toBe(false)
})
it('does not accept another tenant/member or an invented 204 body as a verified write', async () => {
  for (const data of [{ ...member(worker), tenantId: id(99) }, { ...member(worker), id: id(99) }]) {
    const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => String(path) === '/auth/csrf' ? json({ requestToken: 'unit-csrf' }) : json(data))
    await expect(employeesApi(new HttpClient(fetcher)).deactivate(workspace, worker)).rejects.toMatchObject({ outcomeUnknown: true })
  }
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => String(path) === '/auth/csrf' ? json({ requestToken: 'unit-csrf' }) : json({ windows: [] }))
  await expect(employeesApi(new HttpClient(fetcher)).schedule(workspace, worker, [])).rejects.toMatchObject({ outcomeUnknown: true })
})
it.each([400, 401, 403, 404, 409, 429, 503])('preserves HTTP %s without automatic mutation retries', async status => {
  const fake = employeeServer(); fake.state.reject = status
  await expect(employeesApi(new HttpClient(fake.fetcher)).deactivate(workspace, worker)).rejects.toMatchObject({ status, outcomeUnknown: status >= 500 })
  expect(fake.writes()).toHaveLength(1)
})
