// @vitest-environment jsdom
import { act, render, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, expect, it, vi } from 'vitest'
import { RuntimeProvider } from '../../app/providers/runtime'
import { WorkspaceContext } from '../access'
import { HttpClient } from '../../shared/api/http'
import { workspace, id } from '../../test/operations'
import { offlineStorage, scopeKey } from './storage'
import type { OutboxRow } from './storage'
import { envelope } from './offline.test.support'
import { synchronizeOutbox } from './workflow'
import { WorkspaceOutboxSync } from './WorkspaceOutboxSync'
vi.mock('./workflow', () => ({ synchronizeOutbox: vi.fn() }))
afterEach(() => vi.restoreAllMocks())
it('continues across module navigation, keeps original scope and aborts when the authenticated workspace unmounts', async () => {
  const event = envelope(); const scope = { userId: workspace.session.userId, membershipId: workspace.access.membershipId, tenantId: workspace.tenantId, branchId: workspace.branchId }
  const row: OutboxRow = { id: event.eventId, envelope: event, status: 'pending', scope: scopeKey(scope), result: null, attempts: 0, nextAttemptAt: 0 }
  vi.spyOn(offlineStorage, 'outbox').mockResolvedValue([row]); vi.mocked(synchronizeOutbox).mockResolvedValue(false)
  const runtime = { http: new HttpClient(vi.fn()), service: { login: vi.fn(), logout: vi.fn(), session: vi.fn(), tenants: vi.fn(), branches: vi.fn(), access: vi.fn() }, preview: false }
  const view = render(<RuntimeProvider runtime={runtime}><MemoryRouter><WorkspaceContext.Provider value={workspace}><WorkspaceOutboxSync /></WorkspaceContext.Provider></MemoryRouter></RuntimeProvider>)
  await waitFor(() => expect(synchronizeOutbox).toHaveBeenCalledTimes(1))
  const observed = vi.mocked(synchronizeOutbox).mock.calls[0]!
  expect(observed[0]).toEqual(scope); expect(observed[3].aborted).toBe(false)
  act(() => { window.dispatchEvent(new Event('online')) }); await waitFor(() => expect(synchronizeOutbox).toHaveBeenCalledTimes(2))
  expect(vi.mocked(synchronizeOutbox).mock.calls[1]?.[0]).toEqual(scope)
  view.unmount(); expect(observed[3].aborted).toBe(true)
})
it('does not synchronize an empty queue or mix a different branch with the prior scope', async () => {
  vi.mocked(synchronizeOutbox).mockClear(); const read = vi.spyOn(offlineStorage, 'outbox').mockResolvedValue([])
  const runtime = { http: new HttpClient(vi.fn()), service: { login: vi.fn(), logout: vi.fn(), session: vi.fn(), tenants: vi.fn(), branches: vi.fn(), access: vi.fn() }, preview: false }
  const next = { ...workspace, branchId: id(99), branch: { ...workspace.branch, id: id(99) } }
  const view = render(<RuntimeProvider runtime={runtime}><MemoryRouter><WorkspaceContext.Provider value={next}><WorkspaceOutboxSync /></WorkspaceContext.Provider></MemoryRouter></RuntimeProvider>)
  await waitFor(() => expect(read).toHaveBeenCalled())
  expect(read.mock.calls[0]?.[0].branchId).toBe(id(99)); expect(synchronizeOutbox).not.toHaveBeenCalled(); view.unmount()
})
