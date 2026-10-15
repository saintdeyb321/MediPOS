import { useInfiniteQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { useRuntime } from '../access'

export function useTenants() {
  const { service } = useRuntime()
  const client = useQueryClient()
  const navigate = useNavigate()
  const tenants = useInfiniteQuery({
    queryKey: ['tenants'],
    initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam, signal }) => service.tenants(pageParam, signal),
    getNextPageParam: page => page.nextAfterTenantId ?? undefined,
  })
  async function select(id: string) {
    await client.cancelQueries({ queryKey: ['tenant'] })
    client.removeQueries({ queryKey: ['tenant'] })
    navigate(`/boticas/${id}/sucursales`)
  }
  return { tenants, rows: tenants.data?.pages.flatMap(page => page.items) ?? [], select }
}
