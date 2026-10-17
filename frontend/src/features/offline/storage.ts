import { parseExactJson, stringifyExactJson } from '../../shared/api/exactJson'
import { catalogItem } from './api'
import type { CatalogItem, Device, EventEnvelope, Receipt } from './api'

export interface OfflineScope { userId: string; tenantId: string; branchId: string; membershipId: string }
export const scopeKey = (s: OfflineScope) => [s.userId, s.tenantId, s.branchId, s.membershipId].join('/')
export interface OfflineLease extends OfflineScope {
  device: Device; tenantName: string; branchName: string; cashSessionId: string
  expiresAt: number; preparedAt: number; lastObservedAt: number; nonce: string; cursor: string
}
export interface OutboxRow { id: string; scope: string; envelope: EventEnvelope; status: 'pending' | 'accepted' | 'conflict' | 'rejected'; result: Receipt | null; attempts: number; nextAttemptAt: number }
interface CatalogRow { id: string; scope: string; version: string; wire: string }
const databaseName = 'medipos-contingency-v1'
const lockKey = 'medipos.offline.locked'
let memoryLocked = false
function locked() { try { return memoryLocked || localStorage.getItem(lockKey) === 'true' } catch { return true } }
function setLocked(value: boolean) { memoryLocked = value; try { if (value) localStorage.setItem(lockKey, 'true'); else localStorage.removeItem(lockKey) } catch { /* Storage denial never grants a lease. */ } }
function notify() { try { if (typeof BroadcastChannel !== 'undefined') { const channel = new BroadcastChannel(databaseName); channel.postMessage('refresh'); channel.close() } } catch { /* The bounded IDB polling remains available when cross-tab messaging is denied. */ } }
function request<T>(r: IDBRequest<T>): Promise<T> { return new Promise((resolve, reject) => { r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error) }) }
async function transaction<T>(names: string[], mode: IDBTransactionMode, work: (tx: IDBTransaction) => Promise<T>): Promise<T> {
  const opening = indexedDB.open(databaseName, 1)
  opening.onupgradeneeded = () => { const db = opening.result; for (const name of ['meta', 'catalog', 'outbox']) db.createObjectStore(name, name === 'meta' ? undefined : { keyPath: 'id' }) }
  const db = await request(opening)
  try {
    const tx = db.transaction(names, mode, { durability: mode === 'readwrite' ? 'strict' : 'default' })
    const done = new Promise<void>((resolve, reject) => { tx.oncomplete = () => resolve(); tx.onabort = () => reject(tx.error ?? new Error('No se guardó la operación local.')); tx.onerror = () => reject(tx.error) })
    // Observe immediately so an abort cannot become an unhandled rejection during a request failure.
    void done.catch(() => undefined)
    try { const result = await work(tx); await done; return result }
    catch (error) { try { tx.abort() } catch { /* Already completed/aborted. */ } await done.catch(() => undefined); throw error }
  } finally { db.close() }
}
async function active(tx: IDBTransaction): Promise<OfflineLease | null> { return (await request(tx.objectStore('meta').get('active')) as OfflineLease | undefined) ?? null }
export function leaseValid(lease: OfflineLease, now = Date.now()) {
  return now >= lease.lastObservedAt - 1000 && now >= lease.preparedAt - 1000 && now < lease.expiresAt && lease.expiresAt <= lease.preparedAt + 30 * 60_000
}
async function assertLease(tx: IDBTransaction, lease: OfflineLease, now: number) {
  const current = await active(tx)
  if (locked() || !current || current.nonce !== lease.nonce || scopeKey(current) !== scopeKey(lease) || !leaseValid(current, now)) throw new Error('La contingencia venció o cambió de cuenta/sucursal. Reconecta y verifica acceso.')
  return current
}
export const offlineStorage = {
  async lease() { return locked() ? null : transaction(['meta'], 'readonly', active) },
  async registration(scope: OfflineScope): Promise<string | null> { return transaction(['meta'], 'readonly', async tx => (await request(tx.objectStore('meta').get('registration/' + scopeKey(scope))) as string | undefined) ?? null) },
  async reserveRegistration(scope: OfflineScope, label: string) { return transaction(['meta'], 'readwrite', async tx => { const key = 'registration/' + scopeKey(scope); const existing = await request(tx.objectStore('meta').get(key)); if (existing && existing !== label) throw new Error('Concilia el registro previo del dispositivo.'); await request(tx.objectStore('meta').put(label, key)) }) },
  async releaseUnsentRegistration(scope: OfflineScope, label: string) { return transaction(['meta'], 'readwrite', async tx => { const key = 'registration/' + scopeKey(scope); if (await request(tx.objectStore('meta').get(key)) === label) await request(tx.objectStore('meta').delete(key)) }) },
  async revocation(scope: OfflineScope): Promise<string | null> { return transaction(['meta'], 'readonly', async tx => (await request(tx.objectStore('meta').get('revocation/' + scopeKey(scope))) as string | undefined) ?? null) },
  async markRevocation(scope: OfflineScope, id: string | null) { return transaction(['meta'], 'readwrite', async tx => { const key = 'revocation/' + scopeKey(scope); if (id) await request(tx.objectStore('meta').put(id, key)); else await request(tx.objectStore('meta').delete(key)) }) },
  async prepare(lease: OfflineLease, items: CatalogItem[], reset: boolean) {
    return transaction(['meta', 'catalog'], 'readwrite', async tx => {
      const old = await active(tx); const catalog = tx.objectStore('catalog'); const key = scopeKey(lease)
      if (reset || !old || scopeKey(old) !== key) await request(catalog.clear())
      for (const item of items) {
        const id = key + '/' + item.businessProductId; const previous = await request(catalog.get(id)) as CatalogRow | undefined
        if (previous && BigInt(previous.version) >= BigInt(item.version.literal)) continue
        // Keep tombstone version so a late older upsert cannot resurrect an item.
        await request(catalog.put({ id, scope: key, version: item.version.literal, wire: stringifyExactJson(item) } satisfies CatalogRow))
      }
      await request(tx.objectStore('meta').put(lease, 'active'))
    }).then(() => { setLocked(false); notify() })
  },
  async catalog(lease: OfflineLease): Promise<CatalogItem[]> {
    return transaction(['meta', 'catalog'], 'readonly', async tx => {
      await assertLease(tx, lease, Date.now()); const rows = await request(tx.objectStore('catalog').getAll()) as CatalogRow[]
      return rows.filter(row => row.scope === scopeKey(lease)).map(row => catalogItem(parseExactJson(row.wire))).filter(row => row.operation === 'upsert')
    })
  },
  async touch(lease: OfflineLease, now = Date.now()) {
    return transaction(['meta'], 'readwrite', async tx => { const current = await assertLease(tx, lease, now); await request(tx.objectStore('meta').put({ ...current, lastObservedAt: Math.max(now, current.lastObservedAt) }, 'active')) })
  },
  async enqueue(lease: OfflineLease, envelope: EventEnvelope, now = Date.now()) {
    return transaction(['meta', 'outbox'], 'readwrite', async tx => {
      const current = await assertLease(tx, lease, now)
      if (envelope.deviceId !== current.device.deviceId) throw new Error('Dispositivo incorrecto.')
      const rows = await request(tx.objectStore('outbox').getAll()) as OutboxRow[]
      if (rows.filter(row => row.scope === scopeKey(lease) && row.status === 'pending').length >= 200) throw new Error('La cola local alcanzó 200 pendientes. Reconecta antes de continuar.')
      // add, never put: the immutable event UUID/payload cannot be overwritten.
      await request(tx.objectStore('outbox').add({ id: envelope.eventId, scope: scopeKey(lease), envelope, status: 'pending', result: null, attempts: 0, nextAttemptAt: 0 } satisfies OutboxRow))
      await request(tx.objectStore('meta').put({ ...current, lastObservedAt: Math.max(now, current.lastObservedAt) }, 'active'))
    })
  },
  async outbox(scope: OfflineScope, onlineVerified = false): Promise<OutboxRow[]> {
    return transaction(['meta', 'outbox'], 'readonly', async tx => {
      const current = await active(tx)
      if (!onlineVerified && (locked() || !current || scopeKey(current) !== scopeKey(scope) || !leaseValid(current))) throw new Error('La cola requiere verificación de identidad.')
      return (await request(tx.objectStore('outbox').getAll()) as OutboxRow[]).filter(row => row.scope === scopeKey(scope))
    })
  },
  async acknowledge(scope: OfflineScope, results: Receipt[]) {
    return transaction(['outbox'], 'readwrite', async tx => {
      const store = tx.objectStore('outbox')
      for (const result of results) {
        const row = await request(store.get(result.eventId)) as OutboxRow | undefined
        if (!row || row.scope !== scopeKey(scope)) throw new Error('Respuesta de otro contexto.')
        if (row.status !== 'pending') { if (row.status !== result.status || row.result?.resultReferenceId !== result.resultReferenceId) throw new Error('Resultado terminal incompatible.'); continue }
        row.status = result.status; row.result = result; row.attempts++; row.nextAttemptAt = Date.now() + Math.min(60_000, 2000 * 2 ** Math.min(row.attempts, 5))
        await request(store.put(row))
      }
    })
  },
  async defer(scope: OfflineScope, ids: string[], retryAllowed = true) {
    return transaction(['outbox'], 'readwrite', async tx => { const store = tx.objectStore('outbox'); for (const id of ids) { const row = await request(store.get(id)) as OutboxRow | undefined; if (row?.scope === scopeKey(scope) && row.status === 'pending') { row.attempts = retryAllowed ? row.attempts + 1 : Math.max(6, row.attempts + 1); row.nextAttemptAt = Date.now() + Math.min(60_000, 2000 * 2 ** Math.min(row.attempts, 5)); await request(store.put(row)) } } })
  },
  async lock() {
    // This deny-only marker contains no identity/permission/credential. It also blocks reads if IDB cleanup fails.
    setLocked(true); notify()
    if (typeof indexedDB === 'undefined') return
    return transaction(['meta', 'catalog', 'outbox'], 'readwrite', async tx => {
      await request(tx.objectStore('meta').delete('active')); await request(tx.objectStore('catalog').clear())
      const rows = await request(tx.objectStore('outbox').getAll()) as OutboxRow[]
      for (const row of rows) if (row.status !== 'pending') await request(tx.objectStore('outbox').delete(row.id))
      // Pending uncertain writes are quarantined. Only a new real session of the same scope can reconcile them.
    })
  },
}
