export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly traceId?: string
  constructor(status: number, code: string, traceId?: string) {
    super(code); this.name = 'ApiError'; this.status = status; this.code = code; this.traceId = traceId
  }
}
export type UnauthorizedListener = () => void
export class HttpClient {
  private readonly listeners = new Set<UnauthorizedListener>()
  private readonly fetcher: typeof fetch
  constructor(fetcher: typeof fetch = (...args) => fetch(...args)) { this.fetcher = fetcher }
  subscribeUnauthorized(listener: UnauthorizedListener) {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }
  async request(path: string, options: { method?: 'GET' | 'POST' | 'PUT'; body?: unknown; signal?: AbortSignal } = {}): Promise<unknown> {
    // Deliberately relative-only: production uses a single HTTPS origin, not a configurable API host.
    if (!/^\/(api|auth)\//.test(path) || path.includes('\\') || /[\r\n]/.test(path)) throw new ApiError(0, 'client.invalid_path')
    if (window.location.protocol !== 'https:') throw new ApiError(0, 'client.https_required')
    const method = options.method ?? 'GET'
    const headers = new Headers({ Accept: 'application/json' })
    if (method !== 'GET') {
      headers.set('X-CSRF-TOKEN', await this.csrf(options.signal))
      if (options.body !== undefined) headers.set('Content-Type', 'application/json')
    }
    let response: Response
    try {
      response = await this.fetcher(path, {
        method, headers, credentials: 'same-origin', cache: 'no-store', redirect: 'error',
        body: options.body === undefined ? undefined : JSON.stringify(options.body), signal: options.signal,
      })
    } catch (error) {
      if (options.signal?.aborted || error instanceof DOMException && error.name === 'AbortError') throw error
      throw new ApiError(0, 'client.network_error')
    }
    if (response.status === 401) this.listeners.forEach(listener => listener())
    if (response.status === 204 && response.ok) return undefined
    let data: unknown
    try {
      if (!response.headers.get('Content-Type')?.match(/^application\/(json|problem\+json)(;|$)/i)) throw new Error('media')
      data = await response.json()
    } catch (error) {
      if (options.signal?.aborted || error instanceof DOMException && error.name === 'AbortError') throw error
      throw new ApiError(response.ok ? 0 : response.status, response.status === 401 ? 'auth.session_required' : 'client.unexpected_response')
    }
    if (!response.ok) {
      const problem = data && typeof data === 'object' ? data as Record<string, unknown> : {}
      const code = typeof problem.code === 'string' && /^[a-z0-9_.]{1,100}$/.test(problem.code) ? problem.code : `http.${response.status}`
      const trace = typeof problem.traceId === 'string' && /^[a-zA-Z0-9_-]{1,100}$/.test(problem.traceId) ? problem.traceId : undefined
      throw new ApiError(response.status, code, trace)
    }
    return data
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
