import { expect, it, vi } from 'vitest'
import { HttpClient } from '../../shared/api/http'
import { platformApi } from './api'

// Contract regressions only, not real OIDC/PostgreSQL evidence.
const category = { id: '019a0162-0000-7000-8000-000000000001', name: 'Medicamentos', isActive: true, createdAt: '2026-10-18T12:00:00Z' }
it('keeps the idempotency header forbidden outside the two reviewed creation routes', async () => {
  const fetcher = vi.fn<typeof fetch>(); const http = new HttpClient(fetcher)
  for (const path of ['/platform/tenants/unreviewed', '/platform/tenants/with-owner?override=true', '/api/unreviewed'])
    await expect(http.request(path, { method: 'POST', idempotencyKey: category.id })).rejects.toMatchObject({ code: 'client.invalid_request' })
  await expect(http.request('/platform/tenants/with-owner', { method: 'GET', idempotencyKey: category.id })).rejects.toMatchObject({ code: 'client.invalid_request' })
  expect(fetcher).not.toHaveBeenCalled()
})
it('uses the required-owner creation contract with the original idempotency key and CSRF', async () => {
  const license = { tenantId: category.id, licenseId: '019a0162-0000-7000-8000-000000000002', status: 'trial', startsAt: category.createdAt, expiresAt: '2026-11-17T12:00:00Z', maxBranches: 2 }
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => new Response(JSON.stringify(String(path) === '/auth/csrf' ? { requestToken: 'synthetic-csrf' } : license), { status: 200, headers: { 'Content-Type': 'application/json' } }))
  await platformApi(new HttpClient(fetcher)).create({ tradingName: 'Synthetic contract', ownerEmail: 'future@example.invalid', status: 'trial', startsAt: license.startsAt, expiresAt: license.expiresAt, maxBranches: 2 }, category.id)
  const calls = fetcher.mock.calls.filter(([, init]) => init?.method === 'POST')
  expect(calls).toHaveLength(1); expect(calls[0]![0]).toBe('/platform/tenants/with-owner')
  expect(new Headers(calls[0]![1]!.headers).get('Idempotency-Key')).toBe(category.id)
  expect(new Headers(calls[0]![1]!.headers).get('X-CSRF-TOKEN')).toBe('synthetic-csrf')
  expect(JSON.parse(String(calls[0]![1]!.body))).toMatchObject({ ownerEmail: 'future@example.invalid' })
})
it.each([{ ...category, name: 'Otra categoría' }, { ...category, isActive: false }, { ...category, createdAt: 'invalid' }])('keeps a mismatched category response uncertain instead of claiming success', async reply => {
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => new Response(JSON.stringify(String(path) === '/auth/csrf' ? { requestToken: 'synthetic-csrf' } : reply), { status: 200, headers: { 'Content-Type': 'application/json' } }))
  await expect(platformApi(new HttpClient(fetcher)).createCategory('Medicamentos')).rejects.toMatchObject({ code: 'client.outcome_unknown', outcomeUnknown: true })
  expect(fetcher.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1)
})
