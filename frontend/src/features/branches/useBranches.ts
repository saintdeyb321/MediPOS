import { useEffect } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useParams } from 'react-router'
import { isUuid, useRuntime } from '../access'

export function useBranches() {
  const { tenantId = '' } = useParams()
  const { service } = useRuntime()
  const client = useQueryClient()
  useEffect(() => {
    // Entering branch selection retires every previous operational workspace.
    const predicate = (query: { queryKey: readonly unknown[] }) => query.queryKey[0] === 'tenant' && isUuid(query.queryKey[2])
    void client.cancelQueries({ predicate })
    client.removeQueries({ predicate })
  }, [client])
  const valid = isUuid(tenantId)
  const branches = useQuery({ queryKey: ['tenant', tenantId, 'branches'], queryFn: ({ signal }) => service.branches(tenantId, signal), enabled: valid, staleTime: 0 })
  return { tenantId, valid, branches }
}
