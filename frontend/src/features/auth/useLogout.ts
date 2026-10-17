import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { useAuthState, useRuntime } from '../access'
import { offlineStorage } from '../offline'

export function useLogout() {
  const { service } = useRuntime()
  const client = useQueryClient()
  const navigate = useNavigate()
  const auth = useAuthState()
  return useMutation({
    mutationFn: () => {
      // The deny marker is synchronous. A blocked/failed IDB cleanup must never delay server revocation.
      void offlineStorage.lock().catch(() => undefined)
      return service.logout()
    },
    onSuccess: async () => {
      await client.cancelQueries()
      client.clear()
      client.setQueryData(['session'], null)
      auth.reset()
      navigate('/', { replace: true })
    },
  })
}
