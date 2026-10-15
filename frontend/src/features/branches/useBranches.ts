import { useQuery } from '@tanstack/react-query'
import { useParams } from 'react-router'
import { isUuid, useRuntime } from '../access'

export function useBranches() {
  const { tenantId = '' } = useParams()
  const { service } = useRuntime()
  const valid = isUuid(tenantId)
  const branches = useQuery({ queryKey: ['tenant', tenantId, 'branches'], queryFn: ({ signal }) => service.branches(tenantId, signal), enabled: valid, staleTime: 0 })
  return { tenantId, valid, branches }
}
