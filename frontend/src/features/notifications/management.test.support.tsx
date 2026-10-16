import { vi } from 'vitest'
import { transferServer, mountTransfers, id, n, time, workspace, json } from '../transfers/transfers.test.support'
import type { NotificationDetails } from './api'
import type { BranchDetails, LegalEntity, LegalInput, BranchInput, LicenseDetails } from '../settings/api'
import type { ImportJob } from '../product-import/api'
import { parseExactJson } from '../../shared/api/exactJson'

// Synthetic component/transport contracts only. No PostgreSQL or real import evidence.
export { mountTransfers, id, n, time, workspace, json }
export const alert: NotificationDetails = { notificationId: id(500), type: 'expiration_30_days', branchId: workspace.branchId, productId: id(10), productName: 'Producto sintético de alerta', inventoryLotId: id(30), batchNumber: 'BATCH-UNIT', expirationDate: '2026-10-20', daysUntilExpiration: 4, title: 'Alerta unitaria', message: 'Revisar lote unitario', createdAt: time, isRead: false, readAt: null, isCurrentlyActionable: true }
export const legalEntity: LegalEntity = { id: id(4), tenantId: workspace.tenantId, legalName: 'Entidad sintética unitaria', ruc: 'UNIT-NO-FISCAL', createdAt: time }
export const importJob: ImportJob = { importJobId: id(510), status: 'completed', fileName: 'unit.xlsx', templateVersion: '1', totalRows: 2, validRows: 1, importedRows: 1, invalidRows: 1, createdAt: time, completedAt: time, rows: [{ rowNumber: 4, status: 'imported', businessProductId: id(10), errorCode: null, errorMessage: null }, { rowNumber: 5, status: 'invalid', businessProductId: null, errorCode: 'import.unit_invalid', errorMessage: 'Fila sintética inválida' }], nextAfterRowNumber: null }
export function managementServer() {
  const base = transferServer()
  const management = { notifications: [alert], legal: [legalEntity], branches: base.state.branches as BranchDetails[], license: { tenantId: workspace.tenantId, licenseId: id(520), status: 'trial', startsAt: time, expiresAt: '2026-11-16T12:00:00Z', maxBranches: 2 } as LicenseDetails,
    totalCount: n('1'), unread: null as null | ReturnType<typeof n>, reject: 0, failGet: false, lose: '', jobs: [importJob], delay: null as Promise<Response> | null,
    audits: [{ auditId: id(530), actorId: workspace.session.userId, action: 'unit.action', entityType: 'unit_entity', entityId: id(4), occurredAt: time, correlationId: 'unit-correlation', hasBefore: false, hasAfter: true, before: 'CONFIDENTIAL-UNIT-PAYLOAD', after: 'CONFIDENTIAL-UNIT-PAYLOAD' }] }
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (path, init) => {
    const url = new URL(String(path), 'https://unit.test'); const p = url.pathname; const method = init?.method ?? 'GET'; const offset = Number(url.searchParams.get('offset') ?? 0)
    if (p === '/auth/csrf') return json({ requestToken: 'management-unit-csrf' })
    if (method !== 'GET' && management.reject) return json({ code: 'unit.rejected' }, management.reject)
    if (method === 'GET' && management.failGet) return json({ code: 'unit.unavailable' }, 503)
    if (p.endsWith('/notifications/unread-count')) return json({ count: management.unread ?? n(String(management.notifications.filter(row => !row.isRead).length)) })
    if (p.endsWith('/notifications')) {
      const rows = management.notifications.filter(row => (!url.searchParams.get('branchId') || row.branchId === url.searchParams.get('branchId')) && (url.searchParams.get('onlyUnread') !== 'true' || !row.isRead))
      return management.delay ?? json({ rows: rows.slice(offset, offset + 20), totalCount: management.totalCount })
    }
    if (p.includes('/notifications/') && p.endsWith('/read')) { const id = p.split('/').at(-2); const row = management.notifications.find(row => row.notificationId === id)!; management.notifications = management.notifications.map(r => r === row ? { ...r, isRead: true, readAt: time } : r); if (management.lose === 'read') throw new TypeError('Synthetic lost response'); return json({ notificationId: id, readAt: time }) }
    if (p.endsWith('/license')) return json(management.license)
    if (p.endsWith('/legal-entities') || p.endsWith('/branches')) {
      const isLegal = p.endsWith('/legal-entities'); if (method === 'GET') return management.delay ?? json((isLegal ? management.legal : management.branches).slice(offset, offset + Number(url.searchParams.get('limit') ?? 20)))
      const data = parseExactJson(String(init?.body)); const common = { id: id(540), tenantId: workspace.tenantId, createdAt: time }
      const result = isLegal ? { ...common, ...(data as LegalInput) } : { ...common, ...(data as BranchInput), isMainHub: false }
      if ('legalName' in result) management.legal.push(result); else management.branches.push(result)
      if (management.lose === 'create') throw new TypeError('Synthetic lost response')
      return json(result, 201)
    }
    if (p.endsWith('/audit')) return json(management.audits.filter(row => !url.searchParams.get('entityId') || row.entityId === url.searchParams.get('entityId')).slice(offset, offset + 20))
    if (p.endsWith('/product-imports/template')) return new Response('synthetic-xlsx-bytes', { headers: { 'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' } })
    if (p.endsWith('/product-imports')) { if (management.lose === 'import') throw new TypeError('Synthetic lost response'); return json(management.jobs[0], 201) }
    if (p.includes('/product-imports/')) { const job = management.jobs.find(job => p.endsWith(job.importJobId)); if (!job) return json({ code: 'import.job_not_found' }, 404); const after = Number(url.searchParams.get('afterRowNumber') ?? 0); const rows = job.rows.filter(r => r.rowNumber > after); return json({ ...job, rows: rows.slice(0, 20), nextAfterRowNumber: rows.length > 20 ? rows[19]!.rowNumber : null }) }
    return base.fetcher(path, init)
  })
  return { ...base, fetcher, management, writes: () => fetcher.mock.calls.filter(([, init]) => init?.method === 'POST' || init?.method === 'PUT') }
}
