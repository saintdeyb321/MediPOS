import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { ApiError } from '../../shared/api/http'
import { decimalInput, maximumMoney } from '../../shared/api/decimal'
import { cashApi } from './api'

export function useCashSession() {
  const { http } = useRuntime(); const { tenantId, branchId, access, session: identity } = useWorkspace()
  const [parameters, setParameters] = useSearchParams()
  const parametersRef = useRef(parameters)
  useEffect(() => { parametersRef.current = parameters }, [parameters])
  const [pending, setPending] = useState(false); const [error, setError] = useState<unknown>(null)
  const busy = useRef(false); const controller = useRef<AbortController | null>(null)
  useEffect(() => () => controller.current?.abort(), [])
  const uncertain = parameters.has('cashPending')
  const query = useQuery({ queryKey: scopedKey({ tenantId, branchId }, 'current-cash', identity.userId, access.membershipId),
    queryFn: async ({ signal }) => {
      if (!http) throw new ApiError(0, 'client.https_required')
      const result = await cashApi(http).current({ tenantId, branchId }, signal)
      if (result.session && (result.session.membershipId !== access.membershipId || result.session.openedByActorId !== identity.userId)) throw new ApiError(0, 'client.unexpected_response')
      return result
    }, enabled: Boolean(http), retry: false, staleTime: 0, refetchInterval: 30_000,
  })
  const session = !query.isError ? query.data?.session ?? null : null
  const cashMarker = (pending: boolean) => {
    const next = new URLSearchParams(parametersRef.current)
    if (pending) next.set('cashPending', '1'); else next.delete('cashPending')
    parametersRef.current = next; setParameters(next, { replace: true })
  }
  async function refresh() {
    const result = await query.refetch()
    if (result.data?.session && !result.isError) { if (parametersRef.current.has('cashPending')) cashMarker(false); setError(null) }
    return result
  }
  async function open(text: string) {
    const amount = decimalInput(text, maximumMoney, 4, true)
    if (!http || !amount || busy.current || uncertain || query.isFetching || query.isError || !query.data || session) return
    busy.current = true; setPending(true); setError(null)
    controller.current = new AbortController()
    cashMarker(true)
    try {
      await cashApi(http).open({ tenantId, branchId }, amount, controller.current.signal)
      if (controller.current.signal.aborted) return
      await refresh()
    } catch (failure) {
      if (controller.current.signal.aborted) return
      setError(failure)
      if (failure instanceof ApiError && !failure.outcomeUnknown) cashMarker(false)
      if (!controller.current.signal.aborted) await refresh()
    } finally { busy.current = false; setPending(false) }
  }
  return { query, session, pending, error, uncertain, open, refresh, connected: Boolean(http) }
}
export type CashController = ReturnType<typeof useCashSession>
