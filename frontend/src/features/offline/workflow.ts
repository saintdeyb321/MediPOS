import { ApiError, HttpClient } from '../../shared/api/http'
import { add, compare, decimalInput, fits, maximumMoney, maximumQuantity, multiply, roundEven4, zero } from '../../shared/api/decimal'
import type { MediPosService, Workspace } from '../access'
import { cashApi } from '../cash'
import { canonicalJson, payloadHash } from './canonical'
import { offlineApi } from './api'
import type { CatalogItem, EventEnvelope, SalePayloadLine } from './api'
import { leaseValid, offlineStorage, scopeKey } from './storage'
import type { OfflineLease, OfflineScope } from './storage'

export async function prepareOffline(w: Workspace, service: MediPosService, http: HttpClient, signal: AbortSignal): Promise<OfflineLease> {
  if (await offlineStorage.revocation({ ...w, userId: w.session.userId, membershipId: w.access.membershipId })) throw new Error('Concilia primero la revocación original. Una lectura aún activa no demuestra que la revocación falló.')
  if (!('serviceWorker' in navigator) || !navigator.serviceWorker.controller) throw new Error('La PWA aún no controla esta página. Recarga con conexión y comprueba que el navegador permite Service Worker antes de preparar contingencia.')
  const api = offlineApi(http); const old = await offlineStorage.lease()
  const same = old && scopeKey(old) === scopeKey({ ...w, userId: w.session.userId, membershipId: w.access.membershipId })
  let device = same ? old.device : null
  if (!device) {
    const reserved = await offlineStorage.registration({ ...w, userId: w.session.userId, membershipId: w.access.membershipId })
    const label = reserved ?? 'MediPOS navegador ' + crypto.randomUUID()
    await offlineStorage.reserveRegistration({ ...w, userId: w.session.userId, membershipId: w.access.membershipId }, label)
    if (reserved) {
      for (let offset = 0; offset < 10000; offset += 100) {
        const page = await api.devices(w, offset, signal); device = page.find(row => row.deviceLabel === label) ?? null
        if (device || page.length < 100) break
      }
      // A missing read cannot disprove an in-flight registration. Do not create with a new label.
      if (!device) throw new Error('El registro original aún no aparece. Vuelve a consultar; no registraremos otro dispositivo a ciegas.')
    } else {
      try { device = await api.register(w, label, signal) }
      catch (error) {
        // Known pre-send/HTTP rejection is distinct from a lost receipt. Only the former releases this intention.
        if (error instanceof ApiError && !error.outcomeUnknown) await offlineStorage.releaseUnsentRegistration({ ...w, userId: w.session.userId, membershipId: w.access.membershipId }, label)
        throw error
      }
    }
  }
  if (device.status !== 'active') throw new Error('Este dispositivo fue revocado. No puede volver a operar offline.')
  let kind: 'snapshot' | 'changes' = same ? 'changes' : 'snapshot'; let cursor: string | null = same ? old.cursor : null
  let reset = !same; let items: CatalogItem[] = []; let resynced = false; let complete = false
  for (let pages = 0; pages < 200; pages++) {
    const page = await api.catalog(w, device.deviceId, kind, cursor, signal)
    if (page.requiresFullResync) {
      if (resynced) throw new Error('El servidor requiere resincronización. No se renovó la contingencia.')
      resynced = true; reset = true; kind = 'snapshot'; cursor = null; items = []; continue
    }
    if (page.isFullSnapshot !== (kind === 'snapshot')) throw new ApiError(0, 'client.unexpected_response')
    items.push(...page.items); cursor = page.nextCursor
    if (!page.hasMore) { complete = true; break }
  }
  if (!complete || !cursor || signal.aborted) throw new Error('La descarga no terminó. No se sustituyó el catálogo anterior.')
  // Fresh session/access/cash after all pages; cached membership never authorizes a new lease.
  const session = await service.session(signal); const access = await service.access(w.tenantId, w.branchId, signal)
  const cash = (await cashApi(http).current(w, signal)).session
  if (!session?.expiresAtUtc || !session.serverTimeUtc || session.userId !== w.session.userId || !access.isAllowed || access.context?.membershipId !== w.access.membershipId || access.context.userId !== session.userId || !cash || cash.membershipId !== w.access.membershipId) throw new Error('Se requieren sesión vigente, acceso real y caja propia abierta para preparar la contingencia.')
  const now = Date.now(); const server = Date.parse(session.serverTimeUtc)
  if (Math.abs(now - server) > 60_000) throw new Error('Corrige la hora del equipo antes de preparar la contingencia.')
  const remaining = Date.parse(session.expiresAtUtc) - server
  const expiresAt = Math.min(now + remaining, now + 30 * 60_000, access.context.licenseExpiresAt ? now + Date.parse(access.context.licenseExpiresAt) - server : Infinity)
  if (expiresAt <= now || signal.aborted) throw new Error('La sesión/licencia ya no permite preparar contingencia.')
  const lease: OfflineLease = { userId: session.userId, tenantId: w.tenantId, branchId: w.branchId, membershipId: w.access.membershipId,
    device, tenantName: w.tenantName, branchName: w.branch.name, cashSessionId: cash.cashSessionId, expiresAt, preparedAt: now, lastObservedAt: now, nonce: crypto.randomUUID(), cursor }
  await offlineStorage.prepare(lease, items, reset)
  return lease
}

export interface OfflineCartLine { item: CatalogItem; unitId: string; quantity: string }
export function offlineAmounts(lines: OfflineCartLine[]) {
  if (!lines.length || lines.length > 200) throw new Error('Agrega de 1 a 200 líneas.')
  const keys = new Set<string>(); let total = zero
  const payloadLines: SalePayloadLine[] = lines.map(line => {
    const product = line.item.product; const unit = product?.units.find(unit => unit.productUnitId === line.unitId && unit.isActive)
    const quantity = decimalInput(line.quantity, maximumQuantity, 12)
    const key = line.item.businessProductId + '/' + line.unitId
    if (!product?.isActive || !unit || !quantity || keys.has(key)) throw new Error('Revisa producto, presentación y cantidad; no dupliques una línea.')
    keys.add(key); const base = multiply(quantity, unit.conversionToBase)
    if (!fits(base, maximumQuantity, 12)) throw new Error('La conversión de cantidad excede la precisión admitida.')
    total = add(total, roundEven4(multiply(base, product.retailPrice)))
    return { businessProductId: product.businessProductId, productUnitId: unit.productUnitId, quantity, conversionToBaseSnapshot: unit.conversionToBase, unitPriceSnapshot: product.retailPrice, priceKind: 'retail' }
  })
  if (!fits(total, maximumMoney, 4, true)) throw new Error('Total fuera del rango admitido.')
  return { lines: payloadLines, total }
}
export async function queueOfflineSale(lease: OfflineLease, lines: OfflineCartLine[], method: string, payment: string, now = Date.now()): Promise<EventEnvelope> {
  if (!leaseValid(lease, now)) throw new Error('La contingencia venció. Reconecta antes de continuar.')
  if (!['cash', 'yape', 'plin', 'card', 'transfer'].includes(method)) throw new Error('Medio de pago inválido.')
  const amounts = offlineAmounts(lines); const amount = decimalInput(payment, maximumMoney, 4)
  if (!amount || compare(amount, amounts.total) !== 0) throw new Error('El pago debe ser positivo y coincidir exactamente con el total provisional. No se procesa un cobro externo.')
  const payload = canonicalJson({ branchId: lease.branchId, cashSessionId: lease.cashSessionId, lines: amounts.lines, payments: [{ method, amount }], totalAmountSnapshot: amounts.total })
  const at = new Date(now); const lima = new Date(now - 5 * 60 * 60_000).toISOString().slice(0, 10)
  const envelope: EventEnvelope = { eventId: crypto.randomUUID(), deviceId: lease.device.deviceId, eventType: 'offline_sale.v1', schemaVersion: 1,
    clientOccurredAtUtc: at.toISOString(), clientLocalDate: lima, clientTimeZoneId: 'America/Lima', payload, payloadHash: await payloadHash(payload) }
  await offlineStorage.enqueue(lease, envelope, now)
  return envelope
}

// One batch at a time per browser when Web Locks is available. Server UUID idempotency also protects multiple tabs.
export async function synchronizeOutbox(scope: OfflineScope, service: MediPosService, http: HttpClient, signal: AbortSignal, manual = false) {
  const work = async () => {
    if (await offlineStorage.revocation(scope)) throw new Error('La revocación original debe conciliarse antes de enviar esta cola.')
    const all = await offlineStorage.outbox(scope, true)
    const eligible = all.filter(row => row.status === 'pending' && (manual || row.attempts < 6 && row.nextAttemptAt <= Date.now()))
    const first = eligible[0]; if (!first || signal.aborted) return false
    const session = await service.session(signal); const access = await service.access(scope.tenantId, scope.branchId, signal)
    if (session?.userId !== scope.userId || !access.isAllowed || access.context?.membershipId !== scope.membershipId) { await offlineStorage.lock(); throw new Error('La sincronización requiere la misma cuenta y membresía vigentes.') }
    const rows = eligible.filter(row => row.envelope.deviceId === first.envelope.deviceId).slice(0, 50)
    try {
      const result = await offlineApi(http).batch(scope, first.envelope.deviceId, rows.map(row => row.envelope), signal)
      await offlineStorage.acknowledge(scope, result.results)
      return true
    } catch (error) {
      const retryAllowed = error instanceof ApiError && (error.status === 0 || error.status === 429 || error.status >= 500)
      await offlineStorage.defer(scope, rows.map(row => row.id), retryAllowed)
      if (error instanceof ApiError && (error.status === 401 || error.status === 403)) await offlineStorage.lock()
      throw error
    }
  }
  if (navigator.locks) return navigator.locks.request('medipos-outbox-sync', { ifAvailable: true }, async lock => lock && !signal.aborted ? work() : false)
  return work()
}
