import { expect, it, vi } from 'vitest'
import { HttpClient } from '../../shared/api/http'
import { invitationsApi } from './invitations.api'
import { id, json, time, workspace } from './employees.test.support'

const value = { id: id(80), tenantId: id(2), clientRequestId: id(81), role: 'cashier' as const, status: 'accepted_pending_approval' as const, version: 17,
  createdAt: time, expiresAt: '2026-10-16T12:15:00Z', acceptedAt: time, acceptedDisplayName: 'Cuenta de prueba unitaria', acceptedEmail: 'unit@example.test', approvedAt: null, resultMembershipId: null, revokedAt: null, revocationReason: null, canApprove: true }
function setup(response: () => Response | Promise<Response>) {
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => String(path) === '/auth/csrf' ? json({ requestToken: 'unit-csrf' }) : response())
  return { fetcher, api: invitationsApi(new HttpClient(fetcher)) }
}
it('decodes version once, checks tenant and reconciles by original intention without reissuing a ticket', async () => {
  const s = setup(() => json([value])); expect((await s.api.list(workspace, 0, id(81)))[0]?.version).toBe(17)
  expect(s.fetcher.mock.calls[0]?.[0]).toContain('clientRequestId=' + id(81))
  const foreign = setup(() => json([{ ...value, tenantId: id(99) }]))
  await expect(foreign.api.list(workspace)).rejects.toMatchObject({ code: 'client.unexpected_response' })
})
it('creates with a durable intention, exact role, CSRF and a one-time ticket', async () => {
  const s = setup(() => json({ invitation: value, ticket: 'opaque-unit-ticket' }, 201))
  expect((await s.api.create(workspace, id(81), 'cashier')).ticket).toBe('opaque-unit-ticket')
  const write = s.fetcher.mock.calls.find(([, init]) => init?.method === 'POST')!
  expect(write[1]?.body).toBe(`{"clientRequestId":"${id(81)}","role":"cashier"}`)
  expect(new Headers(write[1]?.headers).get('X-CSRF-TOKEN')).toBe('unit-csrf')
})
it('approves only the reviewed accepted identity using expectedVersion; no client userId', async () => {
  const s = setup(() => json({ ...value, status: 'approved', resultMembershipId: id(90), approvedAt: time, canApprove: false }))
  await s.api.approve(workspace, value)
  expect(s.fetcher.mock.calls.find(([, init]) => init?.method === 'POST')?.[1]?.body).toBe('{"expectedVersion":17,"confirmed":true}')
  await expect(s.api.approve(workspace, { ...value, canApprove: false })).rejects.toMatchObject({ code: 'invitations.unavailable' })
})
it.each([403, 409, 429, 503])('never retries approval on HTTP %s', async status => {
  const s = setup(() => json({ code: 'unit.denied' }, status))
  await expect(s.api.approve(workspace, value)).rejects.toMatchObject({ status })
  expect(s.fetcher.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1)
})
it('retains an ambiguous approval for reconciliation instead of sending another write', async () => {
  const s = setup(() => { throw new TypeError('Synthetic response loss') })
  await expect(s.api.approve(workspace, value)).rejects.toMatchObject({ outcomeUnknown: true })
  expect(s.fetcher.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1)
})
it('accepts only a ticket under the current server session; a receipt creates no membership', async () => {
  const s = setup(() => json({ status: 'accepted_pending_approval', expiresAt: value.expiresAt }))
  expect((await s.api.accept('opaque-unit-ticket')).status).toBe('accepted_pending_approval')
  const write = s.fetcher.mock.calls.find(([, init]) => init?.method === 'POST')!
  expect(write[0]).toBe('/api/employee-invitations/accept'); expect(write[1]?.body).toBe('{"ticket":"opaque-unit-ticket"}')
})
