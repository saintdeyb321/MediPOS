import { parseExactJson, stringifyExactJson } from './exactJson'

export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly traceId?: string
  readonly outcomeUnknown: boolean
  constructor(status: number, code: string, traceId?: string, outcomeUnknown = false) {
    super(code); this.name = 'ApiError'; this.status = status; this.code = code; this.traceId = traceId; this.outcomeUnknown = outcomeUnknown
  }
}
export type UnauthorizedListener = () => void
export class HttpClient {
  private readonly listeners = new Set<UnauthorizedListener>()
  private readonly fetcher: typeof fetch
  private activeRequests = 0
  private activePlatformRequests = 0
  private readonly waiting: { platform: boolean; enter: () => void }[] = []
  constructor(fetcher: typeof fetch = (...args) => fetch(...args)) { this.fetcher = fetcher }
  private admit(signal: AbortSignal, platform: boolean): Promise<() => void> {
    // Focus can refresh directory/access/cash/search together. Respect the existing
    // server budgets (4 tenant requests, 2 auth requests) without retrying anything.
    return new Promise((resolve, reject) => {
      const cancel = () => {
        const index = this.waiting.indexOf(entry)
        if (index >= 0) this.waiting.splice(index, 1)
        signal.removeEventListener('abort', cancel); reject(signal.reason); this.dispatch()
      }
      const enter = () => {
        if (signal.aborted) { cancel(); return }
        signal.removeEventListener('abort', cancel); this.activeRequests++; if (platform) this.activePlatformRequests++
        let released = false
        resolve(() => {
          if (released) return
          released = true; this.activeRequests--; if (platform) this.activePlatformRequests--; this.dispatch()
        })
      }
      const entry = { platform, enter }
      if (signal.aborted) { reject(signal.reason); return }
      this.waiting.push(entry); signal.addEventListener('abort', cancel, { once: true }); this.dispatch()
    })
  }
  private dispatch() {
    // Platform keeps its stricter single-request budget; a queued platform request does not block tenant reads.
    while (this.activeRequests < 2) {
      const index = this.waiting.findIndex(entry => !entry.platform || this.activePlatformRequests === 0)
      if (index < 0) return
      this.waiting.splice(index, 1)[0]!.enter()
    }
  }
  subscribeUnauthorized(listener: UnauthorizedListener) {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }
  async request(path: string, options: { method?: 'GET' | 'POST' | 'PUT'; body?: unknown; xlsxBody?: Blob; signal?: AbortSignal; exactNumbers?: boolean; responseType?: 'blob'; timeoutMs?: number; idempotencyKey?: string; intention?: { id: string; branchId: string } } = {}): Promise<unknown> {
    // Deliberately relative-only: production uses a single HTTPS origin, not a configurable API host.
    if (!/^\/(api|auth|platform)\//.test(path) || path.includes('\\') || /[\r\n]/.test(path)) throw new ApiError(0, 'client.invalid_path')
    if (window.location.protocol !== 'https:') throw new ApiError(0, 'client.https_required')
    const url = new URL(path, window.location.origin)
    if (url.origin !== window.location.origin || !/^\/(api|auth|platform)\//.test(url.pathname)) throw new ApiError(0, 'client.invalid_path')
    const method = options.method ?? 'GET'
    if (options.idempotencyKey !== undefined && (method !== 'POST' || path !== '/platform/tenants' ||
      !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(options.idempotencyKey) ||
      options.idempotencyKey === '00000000-0000-0000-0000-000000000000')) throw new ApiError(0, 'client.invalid_request')
    if (options.intention && (method === 'GET' || !/^[0-9a-f-]{36}$/i.test(options.intention.id) || !/^[0-9a-f-]{36}$/i.test(options.intention.branchId))) throw new ApiError(0, 'client.invalid_request')
    if (method === 'GET' && options.body !== undefined) throw new ApiError(0, 'client.invalid_request')
    // B1.6 accepts raw XLSX bytes only, bounded before CSRF or network I/O.
    if (options.xlsxBody !== undefined && (method !== 'POST' || options.body !== undefined || !(options.xlsxBody instanceof Blob) || options.xlsxBody.size < 1 || options.xlsxBody.size > 5 * 1024 * 1024)) throw new ApiError(0, 'client.invalid_request')
    // Encode before CSRF/network I/O. Never round an unsafe JS number into a request.
    const body = options.xlsxBody ?? (options.body === undefined ? undefined : stringifyExactJson(options.body))
    const timeoutMs = options.timeoutMs ?? 15_000
    if (!Number.isInteger(timeoutMs) || timeoutMs < 1000 || timeoutMs > 60_000) throw new ApiError(0, 'client.invalid_request')
    const timeout = AbortSignal.timeout(timeoutMs)
    const signal = options.signal?.aborted ? options.signal : options.signal ? AbortSignal.any([options.signal, timeout]) : timeout
    let sent = false
    let release: (() => void) | undefined
    try {
      const headers = new Headers({ Accept: options.responseType === 'blob' ? 'application/pdf, application/vnd.openxmlformats-officedocument.spreadsheetml.sheet, application/problem+json' : 'application/json' })
      if (options.idempotencyKey !== undefined) headers.set('Idempotency-Key', options.idempotencyKey)
      if (options.intention) { headers.set('Idempotency-Key', options.intention.id); headers.set('X-MediPOS-Branch', options.intention.branchId) }
      if (method !== 'GET') {
        headers.set('X-CSRF-TOKEN', await this.csrf(signal))
        if (options.body !== undefined) headers.set('Content-Type', 'application/json')
        if (options.xlsxBody !== undefined) headers.set('Content-Type', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet')
      }
      let response: Response
      // CSRF acquisition finishes before taking the mutation's slot, avoiding a
      // nested request deadlock. Cancellation while queued never marks a POST sent.
      release = await this.admit(signal, url.pathname.startsWith('/platform/'))
      try {
        sent = true
        response = await this.fetcher(path, {
          method, headers, credentials: 'same-origin', cache: 'no-store', redirect: 'error',
          body, signal,
        })
      } catch (error) {
        if (signal.aborted || error instanceof DOMException && error.name === 'AbortError') throw error
        throw new ApiError(0, method === 'GET' ? 'client.network_error' : 'client.outcome_unknown', undefined, method !== 'GET')
      }
      if (response.status === 401) this.listeners.forEach(listener => listener())
      if (response.status === 204 && response.ok) return undefined
      let data: unknown
      try {
        if (response.ok && options.responseType === 'blob') {
          const type = response.headers.get('Content-Type')?.split(';')[0]?.trim().toLowerCase()
          if (type !== 'application/pdf' && type !== 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet') throw new Error('media')
          return await response.blob()
        }
        if (!response.headers.get('Content-Type')?.match(/^application\/(json|problem\+json)(;|$)/i)) throw new Error('media')
        data = options.exactNumbers ? parseExactJson(await response.text()) : await response.json()
      } catch (error) {
        if (signal.aborted || error instanceof DOMException && error.name === 'AbortError') throw error
        const uncertain = method !== 'GET' && (response.ok || response.status >= 500)
        throw new ApiError(response.ok ? 0 : response.status, uncertain ? 'client.outcome_unknown' : response.status === 401 ? 'auth.session_required' : 'client.unexpected_response', undefined, uncertain)
      }
      if (!response.ok) {
        const problem = data && typeof data === 'object' ? data as Record<string, unknown> : {}
        const code = typeof problem.code === 'string' && /^[a-z0-9_.]{1,100}$/.test(problem.code) ? problem.code : `http.${response.status}`
        const trace = typeof problem.traceId === 'string' && /^[a-zA-Z0-9_-]{1,100}$/.test(problem.traceId) ? problem.traceId : undefined
        throw new ApiError(response.status, code, trace, method !== 'GET' && response.status >= 500)
      }
      return data
    } catch (error) {
      if (options.signal?.aborted) {
        if (sent && method !== 'GET') throw new ApiError(0, 'client.outcome_unknown', undefined, true)
        throw error
      }
      if (timeout.aborted) throw new ApiError(0, sent && method !== 'GET' ? 'client.outcome_unknown' : 'client.timeout', undefined, sent && method !== 'GET')
      throw error
    } finally { release?.() }
  }
  async csrf(signal?: AbortSignal): Promise<string> {
    const data = await this.request('/auth/csrf', { signal })
    if (!data || typeof data !== 'object' || !('requestToken' in data) || typeof data.requestToken !== 'string' || !data.requestToken) throw new ApiError(0, 'client.unexpected_response')
    return data.requestToken
  }
  async login(signal?: AbortSignal) {
    const token = await this.csrf(signal)
    signal?.throwIfAborted()
    // OIDC is a top-level navigation. Fetching the challenge would follow Google's redirect via CORS.
    const form = document.createElement('form')
    form.method = 'POST'; form.action = '/auth/login'; form.hidden = true
    const input = document.createElement('input')
    input.type = 'hidden'; input.name = '__RequestVerificationToken'; input.value = token
    form.append(input); document.body.append(form)
    try { form.submit() } finally { form.remove() }
  }
}
