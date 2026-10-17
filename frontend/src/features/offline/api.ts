import { ApiError, HttpClient } from '../../shared/api/http'
import { array, boolean, branchPath, decimal, nullable, object, oneOf, text, uint, utc, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import type { JsonNumber } from '../../shared/api/exactJson'

const count = uint
const device = object({ deviceId: uuid, branchId: uuid, deviceLabel: text, status: oneOf('active', 'revoked'), registeredAtUtc: utc, revokedAtUtc: nullable(utc) })
export type Device = ReturnType<typeof device>
const unit = object({ productUnitId: uuid, name: text, conversionToBase: decimal, isBaseUnit: boolean, isActive: boolean })
const medicine = object({ activeIngredients: array(text), normalizedStrength: text, dosageForm: text, route: nullable(text), sanitaryRegistration: nullable(text), canonicalDosageForm: nullable(text), canonicalRoute: nullable(text), equivalenceKey: nullable(text), components: array(object({ ingredient: text, strengthNormalized: text })) })
const product = object({ businessProductId: uuid, internalCode: text, name: text, productType: oneOf('medicine', 'retail'), categoryId: uuid, categoryName: text, brandOrLaboratory: text, barcode: nullable(text), retailPrice: decimal, wholesalePrice: nullable(decimal), isActive: boolean, globalProductId: nullable(uuid), medicine: nullable(medicine), units: array(unit) })
const stock = object({ physicalStockBase: decimal, sellableStockBase: decimal, expiredStockBase: decimal, stockAsOfUtc: utc })
export const catalogItem = object({ businessProductId: uuid, version: decimal, operation: oneOf('upsert', 'tombstone'), product: nullable(product), stock: nullable(stock) })
export type CatalogItem = ReturnType<typeof catalogItem>
const catalogPage = object({ schemaVersion: uint, branchId: uuid, serverGeneratedAtUtc: utc, items: array(catalogItem), nextCursor: nullable(text), hasMore: boolean, isFullSnapshot: boolean, requiresFullResync: boolean, stockAsOfUtc: utc, resultCode: nullable(text), stockIsAdvisory: boolean })
export type CatalogPage = ReturnType<typeof catalogPage>
export interface EventEnvelope { eventId: string; deviceId: string; eventType: 'offline_sale.v1'; schemaVersion: 1; clientOccurredAtUtc: string; clientLocalDate: string; clientTimeZoneId: 'America/Lima'; payload: string; payloadHash: string }
const receipt = object({ eventId: uuid, status: oneOf('pending', 'accepted', 'conflict', 'rejected'), resultCode: text, receiptId: nullable(uuid), resultReferenceId: nullable(uuid), receivedAtUtc: nullable(utc), processedAtUtc: nullable(utc), isReplay: boolean, retryable: boolean })
export type Receipt = ReturnType<typeof receipt>
const batch = object({ protocolVersion: uint, deviceId: uuid, serverReceivedAtUtc: utc, results: array(receipt), pendingCount: count, acceptedCount: count, conflictCount: count, rejectedCount: count })
const conflictLine = object({ businessProductId: uuid, productUnitId: uuid, quantity: decimal, conversionToBaseSnapshot: decimal, unitPriceSnapshot: decimal, priceKind: oneOf('retail', 'wholesale') })
const conflict = object({ id: uuid, syncEventId: uuid, clientEventId: uuid, deviceId: uuid, branchId: uuid, conflictType: text, conflictCode: text, status: oneOf('open', 'resolved'), clientOccurredAtUtc: utc, clientLocalDate: text, clientTimeZoneId: text, receivedAtUtc: utc, detectedAtUtc: utc, processedAtUtc: nullable(utc), resolvedAtUtc: nullable(utc), resolvedByMembershipId: nullable(uuid), resolutionCode: nullable(text), resolutionNote: nullable(text), lines: array(conflictLine) })
export type SyncConflict = ReturnType<typeof conflict>
export function offlineApi(http: HttpClient) {
  const root = (w: BranchScope) => '/api/tenants/' + uuid(w.tenantId) + '/sync'
  return {
    async register(w: BranchScope, deviceLabel: string, signal?: AbortSignal) {
      const data = await http.request(branchPath(w) + '/sync/devices', { method: 'POST', body: { deviceLabel }, signal, exactNumbers: true })
      try { const result = device(data); if (result.branchId !== w.branchId || result.deviceLabel !== deviceLabel || result.status !== 'active') throw new Error(); return result }
      catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
    async devices(w: BranchScope, offset = 0, signal?: AbortSignal) {
      const rows = array(device)(await http.request(branchPath(w) + '/sync/devices?' + new URLSearchParams({ offset: String(offset), limit: '100' }), { signal, exactNumbers: true }))
      if (rows.some(row => row.branchId !== w.branchId)) throw new ApiError(0, 'client.unexpected_response')
      return rows
    },
    async revoke(w: BranchScope, id: string, signal?: AbortSignal) {
      const data = await http.request(root(w) + '/devices/' + uuid(id) + '/revoke', { method: 'POST', body: {}, signal, exactNumbers: true })
      try { const result = device(data); if (result.deviceId !== id || result.branchId !== w.branchId || result.status !== 'revoked') throw new Error(); return result }
      catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
    async catalog(w: BranchScope, id: string, kind: 'snapshot' | 'changes', cursor: string | null, signal?: AbortSignal) {
      const q = new URLSearchParams({ branchId: uuid(w.branchId), pageSize: '100' }); if (cursor) q.set('cursor', cursor)
      const page = catalogPage(await http.request(root(w) + '/devices/' + uuid(id) + '/catalog/' + kind + '?' + q, { signal, exactNumbers: true }))
      if (page.schemaVersion !== 1 || page.branchId !== w.branchId || !page.stockIsAdvisory || page.hasMore && !page.nextCursor) throw new ApiError(0, 'client.unexpected_response')
      for (const item of page.items) if (!/^\d+$/.test(item.version.literal) || item.operation === 'upsert' && (!item.product || !item.stock || item.product.businessProductId !== item.businessProductId) || item.operation === 'tombstone' && (item.product || item.stock)) throw new ApiError(0, 'client.unexpected_response')
      return page
    },
    async batch(w: BranchScope, deviceId: string, events: EventEnvelope[], signal?: AbortSignal) {
      if (!events.length || events.length > 50 || new Set(events.map(e => e.eventId)).size !== events.length || events.some(e => e.deviceId !== deviceId)) throw new Error('Invalid outbox batch')
      const result = batch(await http.request(root(w) + '/batches', { method: 'POST', body: { deviceId: uuid(deviceId), protocolVersion: 1, events, batchRequestId: crypto.randomUUID() }, signal, exactNumbers: true }))
      if (result.protocolVersion !== 1 || result.deviceId !== deviceId || result.results.length !== events.length || result.results.some((r, i) => r.eventId !== events[i]?.eventId || r.status === 'accepted' && (!r.receiptId || !r.resultReferenceId || !r.processedAtUtc) || r.status !== 'pending' && r.retryable)) throw new ApiError(0, 'client.outcome_unknown', undefined, true)
      if (result.pendingCount !== result.results.filter(r => r.status === 'pending').length || result.acceptedCount !== result.results.filter(r => r.status === 'accepted').length || result.conflictCount !== result.results.filter(r => r.status === 'conflict').length || result.rejectedCount !== result.results.filter(r => r.status === 'rejected').length) throw new ApiError(0, 'client.outcome_unknown', undefined, true)
      return result
    },
    async conflicts(w: BranchScope, offset: number, status: 'open' | 'resolved', signal?: AbortSignal) {
      const result = object({ items: array(conflict), offset: count, limit: count, hasMore: boolean })(await http.request(root(w) + '/conflicts?' + new URLSearchParams({ branchId: w.branchId, status, offset: String(offset), limit: '20' }), { signal, exactNumbers: true }))
      if (result.items.some(row => row.branchId !== w.branchId)) throw new ApiError(0, 'client.unexpected_response')
      return result
    },
    async resolve(w: BranchScope, id: string, resolutionCode: 'cancelled' | 'reviewed_no_posting', note: string, signal?: AbortSignal) {
      if (!note.trim() || note.trim().length > 512) throw new Error('Escribe un motivo de hasta 512 caracteres.')
      const data = await http.request(root(w) + '/conflicts/' + uuid(id) + '/resolve', { method: 'POST', body: { resolutionCode, note: note.trim() }, signal })
      try {
        const result = object({ conflictId: uuid, status: oneOf('resolved'), resolutionCode: text, resolvedAtUtc: utc, resolvedByMembershipId: uuid })(data)
        if (result.conflictId !== id || result.resolutionCode !== resolutionCode) throw new Error('Invalid resolution receipt')
        return result
      } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
  }
}
export interface SalePayloadLine { businessProductId: string; productUnitId: string; quantity: JsonNumber; conversionToBaseSnapshot: JsonNumber; unitPriceSnapshot: JsonNumber; priceKind: 'retail' | 'wholesale' }
