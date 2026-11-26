import { isUuid } from '../access'

export type Pending = { kind: 'legal' | 'branch'; attempt?: string; acknowledgedId?: string; legalEntityId?: string; digest?: string }
export type Recovery = { pending?: Pending; entityId?: string }
export function structureRecoveryKey(userId: string, membershipId: string, tenantId: string) {
  return `medipos-structure-v1/${userId}/${membershipId}/${tenantId}`
}

export function readStructureRecovery(key: string) {
  try { return localStorage.getItem(key) } catch { return '!' }
}

export function hasPendingStructureRecovery(userId: string, membershipId: string, tenantId: string) {
  return Boolean(parseStructureRecovery(readStructureRecovery(structureRecoveryKey(userId, membershipId, tenantId)))?.pending)
}

export function parseStructureRecovery(raw: string | null): Recovery | null {
  try {
    const value = JSON.parse(raw ?? '{}') as Recovery
    if (!value || typeof value !== 'object' || Array.isArray(value) || value.entityId !== undefined && !isUuid(value.entityId)) return null
    if (value.pending !== undefined && (!value.pending || !['legal', 'branch'].includes(value.pending.kind) ||
      !isUuid(value.pending.attempt) || value.pending.digest !== undefined && !/^[0-9a-f]{64}$/.test(value.pending.digest) || value.pending.legalEntityId !== undefined && !isUuid(value.pending.legalEntityId) || value.pending.acknowledgedId !== undefined && !isUuid(value.pending.acknowledgedId))) return null
    return value
  } catch { return null }
}
