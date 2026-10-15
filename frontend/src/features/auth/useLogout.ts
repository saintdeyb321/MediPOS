import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { useAuthState, useRuntime } from '../access'

export function useLogout() {
  const { service } = useRuntime()
  const client = useQueryClient()
  const navigate = useNavigate()
  const auth = useAuthState()
  return useMutation({
    mutationFn: () => service.logout(),
    onSuccess: async () => {
      await client.cancelQueries()
      client.clear()
      client.setQueryData(['session'], null)
      auth.reset()
      navigate('/', { replace: true })
    },
  })
}
