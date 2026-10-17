// @vitest-environment node
import { expect, it } from 'vitest'
import { runInNewContext } from 'node:vm'
import { workerSource } from '../build/pwa.js'

it('never intercepts private APIs, auth, non-GET or cross-origin requests and caches only static shell assets', async () => {
  const handlers = new Map<string, (event: { request: { url: string; method: string; mode?: string }; respondWith: (p: Promise<unknown>) => void }) => void>()
  const requests: string[] = []
  const self = { location: { origin: 'https://medipos.test' }, addEventListener: (name: string, fn: (event: { request: { url: string; method: string; mode?: string }; respondWith: (p: Promise<unknown>) => void }) => void) => handlers.set(name, fn) }
  const fetcher = (r: { url: string }) => { requests.push(r.url); return Promise.resolve('network') }
  runInNewContext(workerSource(['/index.html', '/assets/app-HASH.js'], 'test'), { self, URL, fetch: fetcher, caches: { open: async () => ({ match: async () => 'shell' }) } })
  let intercepted = 0
  const respondWith = () => { intercepted++ }
  for (const path of ['/api/me/tenants', '/auth/me', '/auth/google/callback', '/platform/tenants', '/health/ready', '/openapi/v1.json', '/API/me/tenants', '/AUTH/me']) handlers.get('fetch')!({ request: { url: 'https://medipos.test' + path, method: 'GET', mode: 'navigate' }, respondWith })
  handlers.get('fetch')!({ request: { url: 'https://other.test/assets/app-HASH.js', method: 'GET' }, respondWith })
  handlers.get('fetch')!({ request: { url: 'https://medipos.test/assets/app-HASH.js', method: 'POST' }, respondWith })
  expect(intercepted).toBe(0); expect(requests).toEqual([])
  handlers.get('fetch')!({ request: { url: 'https://medipos.test/assets/app-HASH.js', method: 'GET' }, respondWith })
  expect(intercepted).toBe(1)
})
