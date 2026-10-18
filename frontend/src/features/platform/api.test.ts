import { expect, it, vi } from 'vitest'
import { HttpClient } from '../../shared/api/http'
import { platformApi } from './api'

// Contract regressions only, not real OIDC/PostgreSQL evidence.
const category = { id: '019a0162-0000-7000-8000-000000000001', name: 'Medicamentos', isActive: true, createdAt: '2026-10-18T12:00:00Z' }
it.each([{ ...category, name: 'Otra categoría' }, { ...category, isActive: false }, { ...category, createdAt: 'invalid' }])('keeps a mismatched category response uncertain instead of claiming success', async reply => {
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => new Response(JSON.stringify(String(path) === '/auth/csrf' ? { requestToken: 'synthetic-csrf' } : reply), { status: 200, headers: { 'Content-Type': 'application/json' } }))
  await expect(platformApi(new HttpClient(fetcher)).createCategory('Medicamentos')).rejects.toMatchObject({ code: 'client.outcome_unknown', outcomeUnknown: true })
  expect(fetcher.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1)
})
