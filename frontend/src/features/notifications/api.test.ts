import { expect, it } from 'vitest'
import { HttpClient } from '../../shared/api/http'
import { notificationCount, notificationsApi } from './api'
import { alert, managementServer, workspace, id, n } from './management.test.support'

it('preserves int64 counts beyond JS safe integers and rejects invalid long values', async () => {
  const fake = managementServer(); fake.management.totalCount = n('9007199254740993'); fake.management.unread = n('9223372036854775807'); const api = notificationsApi(new HttpClient(fake.fetcher))
  expect((await api.list(workspace, workspace.branchId, false, 0)).totalCount.literal).toBe('9007199254740993')
  expect((await api.count(workspace, workspace.branchId)).literal).toBe('9223372036854775807')
  for (const value of ['-1', '1.5', '9223372036854775808']) expect(() => notificationCount(n(value))).toThrow()
})
it('uses actual branch/onlyUnread/server pagination and rejects mismatched response branches', async () => {
  const fake = managementServer(); const api = notificationsApi(new HttpClient(fake.fetcher)); await api.list(workspace, workspace.branchId, true, 20)
  expect(String(fake.fetcher.mock.calls[0]![0])).toContain(`branchId=${workspace.branchId}&onlyUnread=true&offset=20&limit=20`)
  fake.fetcher.mockResolvedValueOnce(new Response(JSON.stringify({ rows: [{ ...alert, branchId: id(900) }], totalCount: 1 }), { headers: { 'Content-Type': 'application/json' } }))
  await expect(api.list(workspace, workspace.branchId, false, 0)).rejects.toMatchObject({ code: 'client.unexpected_response' })
})
it('marks only the chosen notification, sends empty JSON with CSRF and never retries a lost result', async () => {
  const fake = managementServer(); fake.management.lose = 'read'; const api = notificationsApi(new HttpClient(fake.fetcher))
  await expect(api.read(workspace, alert.notificationId)).rejects.toMatchObject({ outcomeUnknown: true }); expect(fake.writes()).toHaveLength(1)
  const init = fake.writes()[0]![1]!; expect(init.body).toBe('{}'); expect(new Headers(init.headers).get('X-CSRF-TOKEN')).toBe('management-unit-csrf')
  expect((await api.list(workspace, workspace.branchId, false, 0)).rows[0]!.isRead).toBe(true)
})
