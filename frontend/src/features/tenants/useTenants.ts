import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useSession, useTenantDirectory } from '../access'

export function useTenants() {
  const session = useSession()
  const client = useQueryClient()
  const tenants = useTenantDirectory(session.data)
  useEffect(() => {
    void client.cancelQueries({ queryKey: ['tenant'] })
    client.removeQueries({ queryKey: ['tenant'] })
  }, [client])
  return { tenants, rows: tenants.data?.pages.flatMap(page => page.items) ?? [] }
}
