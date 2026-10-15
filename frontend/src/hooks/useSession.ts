import { useContext } from 'react'
import { useQuery } from '@tanstack/react-query'
import { AuthContext, RuntimeContext, WorkspaceContext } from '../app/contexts'
export function useRuntime() {
  const value = useContext(RuntimeContext)
  if (!value) throw new Error('Runtime provider missing')
  return value
}
export function useWorkspace() {
  const value = useContext(WorkspaceContext)
  if (!value) throw new Error('Workspace provider missing')
  return value
}
export const useAuthState = () => useContext(AuthContext)
export function useSession() {
  const { service } = useRuntime()
  const { expired } = useAuthState()
  return useQuery({ queryKey: ['session'], queryFn: ({ signal }) => service.session(signal), enabled: !expired, staleTime: 0, refetchInterval: 60_000 })
}
