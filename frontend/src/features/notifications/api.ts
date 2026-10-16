import { array, boolean, dateOnly, decimal, nullable, object, text, utc, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import { ApiError, HttpClient } from '../../shared/api/http'
import { JsonNumber } from '../../shared/api/exactJson'

export const notificationCount = (value: unknown): JsonNumber => {
  const result = decimal(value)
  if (!/^\d+$/.test(result.literal) || BigInt(result.literal) > 9223372036854775807n) throw new ApiError(0, 'client.unexpected_response')
  return result
}
const days = (value: unknown) => { const v = decimal(value).literal; if (!/^-?\d+$/.test(v) || BigInt(v) < -2147483648n || BigInt(v) > 2147483647n) throw new ApiError(0, 'client.unexpected_response'); return Number(v) }
const notification = object({ notificationId: uuid, type: text, branchId: uuid, productId: uuid, productName: nullable(text), inventoryLotId: uuid, batchNumber: nullable(text), expirationDate: dateOnly, daysUntilExpiration: days, title: text, message: text, createdAt: utc, isRead: boolean, readAt: nullable(utc), isCurrentlyActionable: boolean })
const page = object({ rows: array(notification), totalCount: notificationCount })
export type NotificationDetails = ReturnType<typeof notification>
export function notificationsApi(http: HttpClient) {
  const root = (w: BranchScope) => `/api/tenants/${uuid(w.tenantId)}/notifications`
  const branch = (id: string) => id ? `branchId=${uuid(id)}` : ''
  return {
    async list(w: BranchScope, branchId: string, onlyUnread: boolean, offset: number, signal?: AbortSignal) {
      if (!Number.isInteger(offset) || offset < 0 || offset > 10000) throw new RangeError('Invalid offset')
      const result = page(await http.request(`${root(w)}?${branch(branchId)}&onlyUnread=${onlyUnread}&offset=${offset}&limit=20`, { signal, exactNumbers: true }))
      if (result.rows.length > 20 || result.rows.some(row => branchId && row.branchId !== branchId || onlyUnread && row.isRead || row.isRead !== Boolean(row.readAt))) throw new ApiError(0, 'client.unexpected_response')
      return result
    },
    async count(w: BranchScope, branchId: string, signal?: AbortSignal) { return object({ count: notificationCount })(await http.request(`${root(w)}/unread-count?${branch(branchId)}`, { signal, exactNumbers: true })).count },
    async read(w: BranchScope, id: string, signal?: AbortSignal) {
      const data = await http.request(`${root(w)}/${uuid(id)}/read`, { method: 'POST', body: {}, signal, exactNumbers: true })
      try { const value = object({ notificationId: uuid, readAt: utc })(data); if (value.notificationId !== id) throw new Error('id'); return value }
      catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
  }
}
