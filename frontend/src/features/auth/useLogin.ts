import { useEffect, useRef } from 'react'
import { useMutation } from '@tanstack/react-query'
import { useAuthState, useRuntime, useSession } from '../access'

export function useLogin() {
  const { service, preview } = useRuntime()
  const { expired } = useAuthState()
  const session = useSession()
  const loginRequest = useRef<AbortController | null>(null)
  useEffect(() => () => loginRequest.current?.abort(), [])
  const login = useMutation({ mutationFn: () => {
    loginRequest.current?.abort()
    loginRequest.current = new AbortController()
    return service.login(loginRequest.current.signal)
  } })
  return { preview, expired, session, login }
}
