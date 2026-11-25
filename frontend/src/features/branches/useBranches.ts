import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useParams } from 'react-router'
import { isUuid, useBranchDirectory, useSession } from '../access'

export function useBranches() {
  const { tenantId = '' } = useParams()
  const session = useSession()
  const client = useQueryClient()
  useEffect(() => {
    const predicate = (query: { queryKey: readonly unknown[] }) => query.queryKey[0] === 'tenant' && isUuid(query.queryKey[2])
    void client.cancelQueries({ predicate })
    client.removeQueries({ predicate })
  }, [client, tenantId])
  const branches = useBranchDirectory(tenantId, session.data, true)
  return { tenantId, valid: isUuid(tenantId), branches, session }
}
