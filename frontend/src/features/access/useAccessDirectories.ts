import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import type { Branch, OperationalAccess, Session } from './contracts'
import { isUuid } from './contracts'
import { useRuntime } from './useSession'

export function useTenantDirectory(session: Session | null | undefined) {
  const { service } = useRuntime()
  return useInfiniteQuery({
    queryKey: ['tenants', session?.userId, session?.expiresAtUtc],
    initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam, signal }) => service.tenants(pageParam, signal),
    getNextPageParam: page => page.nextAfterTenantId ?? undefined,
    enabled: Boolean(session), staleTime: 30_000, refetchOnReconnect: 'always',
  })
}

export function branchDirectoryKey(tenantId: string, session: Session | null | undefined) {
  return ['tenant', tenantId, 'branches', session?.userId, session?.expiresAtUtc] as const
}

export function useBranchDirectory(tenantId: string, session: Session | null | undefined, freshOnEntry = false) {
  const { service } = useRuntime()
  return useQuery<Branch[]>({
    queryKey: branchDirectoryKey(tenantId, session),
    queryFn: ({ signal }) => service.branches(tenantId, signal),
    enabled: Boolean(session) && isUuid(tenantId), staleTime: 30_000,
    refetchOnMount: freshOnEntry ? 'always' : true, refetchOnReconnect: 'always',
  })
}

export function useTenantAccess(tenantId: string, session: Session | null | undefined, enabled = true) {
  const { service } = useRuntime()
  return useQuery({
    queryKey: ['tenant', tenantId, 'tenant-access', session?.userId, session?.expiresAtUtc],
    queryFn: ({ signal }) => service.access(tenantId, null, signal),
    enabled: Boolean(session) && isUuid(tenantId) && enabled,
    staleTime: 0, refetchOnMount: 'always', refetchInterval: 60_000, refetchOnReconnect: 'always',
  })
}

export function verifiedAccess(result: OperationalAccess | undefined, session: Session | null | undefined, tenantId: string, branchId: string | null) {
  const context = result?.context
  if (context?.userId === session?.userId && context && context.tenantId === tenantId &&
    context.branchId === branchId && context.membershipIsActive) return context
  return null
}
