// F1 subset of the B8.7 HTTP contracts. UUIDs select resources, never identity or permission.
export type TenantRole = 'owner' | 'pharmacist' | 'cashier'
export interface Session { userId: string }
export interface TenantSummary { tenantId: string; tradingName: string; role: TenantRole }
export interface TenantPage { items: TenantSummary[]; nextAfterTenantId: string | null }
export interface Branch {
  id: string; tenantId: string; legalEntityId: string; name: string
  isMainHub: boolean; createdAt: string
}
export interface AccessContext {
  userId: string; tenantId: string; membershipId: string; role: TenantRole; branchId: string | null
  membershipIsActive: boolean; branchAssigned: boolean; licenseStatus: string | null
  licenseStartsAt: string | null; licenseExpiresAt: string | null; withinSchedule: boolean
  evaluatedAtUtc: string
}
export interface OperationalAccess { isAllowed: boolean; code: string; context: AccessContext | null }
export interface MediPosService {
  session(signal?: AbortSignal): Promise<Session | null>
  login(signal?: AbortSignal): Promise<void>
  logout(signal?: AbortSignal): Promise<void>
  tenants(after?: string, signal?: AbortSignal): Promise<TenantPage>
  branches(tenantId: string, signal?: AbortSignal): Promise<Branch[]>
  access(tenantId: string, branchId: string, signal?: AbortSignal): Promise<OperationalAccess>
}
export const roleLabels: Record<TenantRole, string> = {
  owner: 'Propietario', pharmacist: 'Químico farmacéutico', cashier: 'Cajero',
}
export const isUuid = (value: unknown): value is string => typeof value === 'string' &&
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) &&
  value !== '00000000-0000-0000-0000-000000000000'
