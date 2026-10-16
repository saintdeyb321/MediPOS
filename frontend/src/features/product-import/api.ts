import { array, nullable, object, oneOf, text, uint, utc, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import { ApiError, HttpClient } from '../../shared/api/http'

// ProductImportTemplate / ProductImportLimits (B1.6), not official pharmaceutical staging.
export const excelType = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
export const maximumFileBytes = 5 * 1024 * 1024
const row = object({ rowNumber: uint, status: oneOf('imported', 'invalid'), businessProductId: nullable(uuid), errorCode: nullable(text), errorMessage: nullable(text) })
const job = object({ importJobId: uuid, status: oneOf('processing', 'completed', 'failed'), fileName: text, templateVersion: text, totalRows: uint, validRows: uint, importedRows: uint, invalidRows: uint, createdAt: utc, completedAt: nullable(utc), rows: array(row), nextAfterRowNumber: nullable(uint) })
export type ImportJob = ReturnType<typeof job>
export function validFile(file: File) { return /\.xlsx$/i.test(file.name) && file.name.length <= 256 && ![...file.name].some(c => c === '/' || c === '\\' || c.charCodeAt(0) < 32 || c.charCodeAt(0) === 127) && (!file.type || file.type === excelType) && file.size > 0 && file.size <= maximumFileBytes }
function decode(data: unknown, after = 0, limit = 20) {
  const result = job(data)
  if (result.rows.length > limit || result.totalRows > 5000 || result.rows.some((r, i) => r.rowNumber < 4 || r.rowNumber <= after || r.rowNumber > 5003 || i > 0 && r.rowNumber <= result.rows[i - 1]!.rowNumber || r.status === 'imported' && !r.businessProductId || r.status === 'invalid' && !r.errorCode) || result.nextAfterRowNumber !== null && (!result.rows.length || result.nextAfterRowNumber !== result.rows.at(-1)!.rowNumber)) throw new ApiError(0, 'client.unexpected_response')
  return result
}
export function importApi(http: HttpClient) {
  const root = (w: BranchScope) => `/api/tenants/${uuid(w.tenantId)}/product-imports`
  return {
    async template(w: BranchScope, signal?: AbortSignal) { const data = await http.request(root(w) + '/template', { signal, responseType: 'blob' }); if (!(data instanceof Blob) || data.type !== excelType || !data.size || data.size > maximumFileBytes) throw new ApiError(0, 'client.unexpected_response'); return data },
    async create(w: BranchScope, file: File, signal?: AbortSignal) {
      if (!validFile(file)) throw new ApiError(0, 'client.invalid_file')
      let data: unknown
      try { data = await http.request(root(w) + '?fileName=' + encodeURIComponent(file.name), { method: 'POST', xlsxBody: file, signal, exactNumbers: true, timeoutMs: 60_000 }) }
      catch (failure) {
        // Authorization is rechecked between committed rows. A mid-import 403 can
        // leave a failed job with imported products, unlike a boundary rejection.
        if (failure instanceof ApiError && failure.status === 403) throw new ApiError(failure.status, failure.code, failure.traceId, true)
        throw failure
      }
      // Creation returns up to 100 rows (handler default), unlike our GET pages of 20.
      try { return decode(data, 0, 100) }
      catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
    async read(w: BranchScope, id: string, after: number, signal?: AbortSignal) {
      if (!Number.isInteger(after) || after < 0 || after > 5003) throw new RangeError('Invalid cursor')
      const result = decode(await http.request(`${root(w)}/${uuid(id)}?afterRowNumber=${after}&limit=20`, { signal, exactNumbers: true }), after)
      if (result.importJobId !== id) throw new ApiError(0, 'client.unexpected_response')
      return result
    },
  }
}
