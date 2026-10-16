import { waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { HttpClient } from './http'

const type = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
it('sends the original XLSX bytes, exact MIME, same-origin credentials and a fresh CSRF token', async () => {
  const file = new File(['SYNTHETIC-XLSX-BINARY-UNIT'], 'unit.xlsx', { type }); const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => path === '/auth/csrf' ? json({ requestToken: 'upload-unit-csrf' }) : json({ id: 'unit' }))
  await new HttpClient(fetcher).request('/api/product-imports?fileName=unit.xlsx', { method: 'POST', xlsxBody: file })
  const options = fetcher.mock.calls[1]![1]!; expect(options.body).toBe(file); expect(options.credentials).toBe('same-origin'); expect(options.redirect).toBe('error'); expect(options.cache).toBe('no-store'); expect(new Headers(options.headers).get('Content-Type')).toBe(type); expect(new Headers(options.headers).get('X-CSRF-TOKEN')).toBe('upload-unit-csrf'); expect(fetcher).toHaveBeenCalledTimes(2)
})
it('rejects empty/oversized XLSX and incompatible JSON/method before CSRF or I/O', async () => {
  const fetcher = vi.fn<typeof fetch>(); const http = new HttpClient(fetcher); const file = new Blob(['unit'])
  for (const options of [{ method: 'GET' as const, xlsxBody: file }, { method: 'POST' as const, xlsxBody: file, body: {} }, { method: 'POST' as const, xlsxBody: new Blob([]) }, { method: 'POST' as const, xlsxBody: new Blob([new Uint8Array(5 * 1024 * 1024 + 1)]) }]) await expect(http.request('/api/import', options)).rejects.toMatchObject({ code: 'client.invalid_request' })
  expect(fetcher).not.toHaveBeenCalled()
})
it.each([400, 413, 415, 429, 503])('preserves upload HTTP %i without retry and marks 5xx uncertain', async status => {
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => path === '/auth/csrf' ? json({ requestToken: 'upload-unit-csrf' }) : json({ code: 'import.unit_rejection' }, status)); await expect(new HttpClient(fetcher).request('/api/import', { method: 'POST', xlsxBody: new Blob(['unit']) })).rejects.toMatchObject({ status, code: 'import.unit_rejection', outcomeUnknown: status >= 500 }); expect(fetcher).toHaveBeenCalledTimes(2)
})
it('marks an aborted dispatched binary mutation unknown, without resending it', async () => {
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (path, init) => path === '/auth/csrf' ? json({ requestToken: 'upload-unit-csrf' }) : new Promise((_resolve, reject) => init?.signal?.addEventListener('abort', () => reject(init.signal?.reason), { once: true })))
  const abort = new AbortController(); const promise = new HttpClient(fetcher).request('/api/import', { method: 'POST', xlsxBody: new Blob(['unit']), signal: abort.signal }); const check = expect(promise).rejects.toMatchObject({ outcomeUnknown: true }); await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(2)); abort.abort(); await check; expect(fetcher).toHaveBeenCalledTimes(2)
})
