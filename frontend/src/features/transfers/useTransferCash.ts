import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useRuntime, useWorkspace } from '../access'
import { cashApi } from '../cash'
import { scopedKey } from '../../shared/api/contracts'
import { ApiError } from '../../shared/api/http'
import { equalId } from './api'

// Only the current, operationally authorized branch. Cross-branch receipt requires switching workspace.
export function useTransferCash() {
  const w = useWorkspace(); const { http } = useRuntime(); const [offset, setOffset] = useState(0); const [id, setId] = useState('')
  const choices = useQuery({ queryKey: scopedKey(w, 'transfer-cash-choices', w.session.userId, w.access.membershipId, offset), queryFn: async ({ signal }) => {
    if (!http) throw new ApiError(0, 'client.https_required')
    if (w.access.role === 'owner') return (await cashApi(http).active(w, offset, signal)).map(row => ({ id: row.cashSessionId, label: row.displayName + ' · apertura UTC ' + row.openedAt }))
    const result = await cashApi(http).current(w, signal)
    if (!result.session) return []
    if (!equalId(result.session.membershipId, w.access.membershipId) || !equalId(result.session.openedByActorId, w.session.userId)) throw new ApiError(0, 'client.unexpected_response')
    return [{ id: result.session.cashSessionId, label: 'Tu caja · apertura UTC ' + result.session.openedAt }]
  }, enabled: Boolean(http), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const selected = !choices.isError && !choices.isFetching && choices.data?.some(row => equalId(row.id, id))
  const status = useQuery({ queryKey: scopedKey(w, 'transfer-cash-status', w.session.userId, w.access.membershipId, id), queryFn: async ({ signal }) => {
    if (!http) throw new ApiError(0, 'client.https_required')
    const value = await cashApi(http).status(w, id, signal)
    if (w.access.role !== 'owner' && !equalId(value.membershipId, w.access.membershipId)) throw new ApiError(0, 'client.unexpected_response')
    return value
  }, enabled: Boolean(selected), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const session = selected && !status.isError && !status.isFetching && status.data?.status === 'open' ? status.data : null
  return { choices, status, session, id, setId, offset, setOffset, owner: w.access.role === 'owner' }
}
