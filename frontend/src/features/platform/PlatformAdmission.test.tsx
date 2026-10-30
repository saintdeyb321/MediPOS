import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { focusManager } from '@tanstack/react-query'
import { afterEach, expect, it, vi } from 'vitest'
import { MemoryRouter, useLocation } from 'react-router'
import { RuntimeProvider } from '../../app/providers/runtime'
import { createMediPosService } from '../access'
import { HttpClient } from '../../shared/api/http'
import { PlatformPage } from './PlatformPage'

// Transport-only regressions: no real identity, PostgreSQL or commercial writes.
const id = (n: number) => `019a0162-0000-7000-8000-${String(n).padStart(12, '0')}`
const time = '2026-10-17T12:00:00Z'
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
function Location() { return <output data-testid="location">{useLocation().search}</output> }
function mount(query = '') {
  const state = { admission: 200 }
  const tenant = { tenantId: id(2), tradingName: 'Synthetic admission regression', createdAt: time, administration: null }
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (path, init) => {
    if (init?.method !== 'GET') throw new Error('This read-only regression must never write')
    if (path === '/auth/me') return json({ userId: id(1) })
    if (path === '/platform/session') return state.admission === 200 ? json({ allowed: true }) : json({ code: state.admission === 429 ? 'platform.rate_limited' : 'access.platform_denied' }, state.admission)
    if (String(path).startsWith('/platform/tenants?')) return json({ items: [tenant], nextAfterTenantId: null })
    if (path === `/platform/tenants/${id(2)}/details`) return json({ tenant, license: { tenantId: id(2), licenseId: id(3), status: 'trial', startsAt: time, expiresAt: '2026-11-16T12:00:00Z', maxBranches: 2 }, initialOwnerUserId: null })
    if (path === `/platform/tenants/${id(2)}/initial-owner`) return json({ invitation: null })
    return json({ code: 'unit.not_found' }, 404)
  })
  const http = new HttpClient(fetcher)
  render(<RuntimeProvider runtime={{ service: createMediPosService(http), http, preview: false }}><MemoryRouter initialEntries={['/plataforma' + query]}><PlatformPage /><Location /></MemoryRouter></RuntimeProvider>)
  return { state, fetcher, admissions: () => fetcher.mock.calls.filter(([path]) => path === '/platform/session').length, lists: () => fetcher.mock.calls.filter(([path]) => String(path).startsWith('/platform/tenants?')).length }
}
afterEach(() => { cleanup(); vi.useRealTimers(); focusManager.setFocused(undefined) })

it('does not spend the administrative quota again when browser focus changes repeatedly', async () => {
  const app = mount()
  await screen.findByRole('button', { name: 'Nueva botica' })
  for (let i = 0; i < 4; i++) {
    await act(async () => { focusManager.setFocused(false); focusManager.setFocused(true); await new Promise(resolve => setTimeout(resolve, 10)) })
  }
  expect(app.admissions()).toBe(1)
})

it('waits after a 429 without polling, preserves pending intent and recovers only on an explicit read', async () => {
  vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] })
  const app = mount(`?creationKey=${id(8)}&platformPending=create`); app.state.admission = 429
  await screen.findByText(/Alcanzaste el límite administrativo/)
  const retry = screen.getByRole('button', { name: 'Reconsultar acceso' })
  expect(retry).toBeDisabled()
  expect(screen.queryByText(/requiere activación protegida/)).not.toBeInTheDocument()
  await act(async () => { focusManager.setFocused(false); focusManager.setFocused(true); vi.advanceTimersByTime(61_000) })
  expect(app.admissions()).toBe(1)
  expect(app.lists()).toBe(0)
  expect(retry).toBeEnabled()
  expect(screen.getByTestId('location')).toHaveTextContent(`creationKey=${id(8)}`)
  app.state.admission = 200; fireEvent.click(retry)
  await screen.findByRole('heading', { name: 'Administración de plataforma' })
  expect(app.admissions()).toBe(2)
  expect(screen.getByTestId('location')).toHaveTextContent('platformPending=create')
  expect(screen.getByRole('button', { name: 'Nueva botica' })).toBeDisabled()
  expect(app.fetcher.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true)
})

it('reuses a fresh directory when selecting a tenant instead of fetching the same page again', async () => {
  const app = mount()
  await screen.findByRole('button', { name: 'Gestionar Synthetic admission regression' })
  expect(app.lists()).toBe(1)
  fireEvent.click(screen.getByRole('button', { name: 'Gestionar Synthetic admission regression' }))
  await screen.findByText(/Botica anterior sin asignación pendiente/)
  expect(app.lists()).toBe(1)
})

it('still revalidates authority periodically and blocks the console on a real 403', async () => {
  vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] })
  const app = mount()
  await screen.findByRole('button', { name: 'Nueva botica' })
  app.state.admission = 403
  await act(async () => { vi.advanceTimersByTime(60_000) })
  await screen.findByText(/requiere activación protegida/)
  expect(screen.queryByRole('button', { name: 'Nueva botica' })).not.toBeInTheDocument()
  await waitFor(() => expect(app.admissions()).toBe(2))
})
