import { useEffect } from 'react'
import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import { useLocation, useParams } from 'react-router'
import { isUuid } from './contracts'
import { useRuntime, useSession } from './useSession'

export function useOperationalWorkspace() {
  const { tenantId = '', branchId = '' } = useParams(); const { service } = useRuntime()
  const { pathname } = useLocation(); const session = useSession()
  const valid = isUuid(tenantId) && isUuid(branchId)
  const client = useQueryClient()
  useEffect(() => {
    const predicate = (query: { queryKey: readonly unknown[] }) => query.queryKey[0] === 'tenant' && isUuid(query.queryKey[2]) &&
      (query.queryKey[1] !== tenantId || query.queryKey[2] !== branchId)
    void client.cancelQueries({ predicate })
    client.removeQueries({ predicate })
  }, [client, tenantId, branchId])
  const tenants = useInfiniteQuery({ queryKey: ['tenants'], initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam, signal }) => service.tenants(pageParam, signal), getNextPageParam: page => page.nextAfterTenantId ?? undefined,
  })
  const branches = useQuery({ queryKey: ['tenant', tenantId, 'branches'], queryFn: ({ signal }) => service.branches(tenantId, signal), enabled: valid, staleTime: 0 })
  // Recheck on every route, remount, focus and at least once per minute. This is presentation only;
  // every later business request still requires the backend's own authorization.
  const access = useQuery({ queryKey: ['tenant', tenantId, branchId, 'access', pathname], queryFn: ({ signal }) => service.access(tenantId, branchId, signal), enabled: valid, staleTime: 0, refetchOnMount: 'always', refetchInterval: 60_000 })
  return { tenantId, branchId, valid, tenants, branches, access, session }
}
