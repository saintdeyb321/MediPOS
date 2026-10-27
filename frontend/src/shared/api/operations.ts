import { branchPath, object, text, uuid } from './contracts'
import type { BranchScope } from './contracts'
import { parseExactJson } from './exactJson'
import { ApiError, HttpClient } from './http'

export interface OperationIntention { id: string; branchId: string }
export async function recoverOperation(http: HttpClient, scope: BranchScope, id: string, kind: string, signal?: AbortSignal): Promise<unknown> {
  const receipt = object({ requestId: uuid, kind: text, resultJson: text })(await http.request(`${branchPath(scope)}/operations/${uuid(id)}`, { signal }))
  if (receipt.requestId.toLowerCase() !== id.toLowerCase() || receipt.kind !== kind) throw new ApiError(0, 'client.unexpected_response')
  return parseExactJson(receipt.resultJson)
}
