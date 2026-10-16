import { useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { salesApi } from './sales.api'
import type { SalePaymentInput } from './sales.api'
import { ticketsApi } from './tickets.api'
import type { DraftController } from './useSaleDraft'

export function useSaleCheckout(draft: DraftController) {
  const { http } = useRuntime(); const { tenantId, branchId, session, access } = useWorkspace()
  const client = useQueryClient()
  const [busy, setBusy] = useState(false); const running = useRef(false)
  const [error, setError] = useState<unknown>(null); const [notice, setNotice] = useState('')
  const [denied, setDenied] = useState(false)
  const controller = useRef<AbortController | null>(null)
  const mounted = useRef(true)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; controller.current?.abort() } }, [])
  const sale = draft.server
  const terminal = sale?.status === 'confirmed' || sale?.status === 'voided'
  const ticket = useQuery({ queryKey: scopedKey({ tenantId, branchId }, 'ticket', session.userId, access.membershipId, sale?.saleId),
    queryFn: ({ signal }) => {
      if (!http || !sale) throw new ApiError(0, 'client.unexpected_response')
      return ticketsApi(http).read({ tenantId, branchId }, sale.saleId, signal)
    }, enabled: Boolean(http && terminal), retry: false, staleTime: 0,
  })
  async function reconcile() {
    const state = await draft.refresh()
    if (!mounted.current || !state) return false
    if (state.status !== 'draft') {
      draft.markCheckout(false); setError(null)
      setNotice(state.status === 'confirmed' ? 'Venta confirmada en el servidor. Consulta su ticket interno; no repitas el cobro.' : 'La venta está anulada. No puede cobrarse de nuevo.')
      // Refresh only this branch's commercial reads; no mutation is retried.
      for (const resource of ['product-search', 'current-cash', 'stock']) {
        void client.invalidateQueries({ queryKey: scopedKey({ tenantId, branchId }, resource) })
      }
      return true
    }
    return false
  }
  async function confirm(payments: SalePaymentInput[]) {
    if (!http || !sale || !draft.canCheckout || denied || running.current) return
    running.current = true; setBusy(true); setError(null); setNotice(''); draft.markCheckout(true)
    controller.current = new AbortController()
    try {
      await salesApi(http).confirm({ tenantId, branchId }, sale.saleId, sale.version, payments, controller.current.signal)
      if (!mounted.current) return
      if (!await reconcile() && mounted.current) setNotice('El servidor respondió al envío, pero falta verificar el estado. Consulta esta misma venta; no vuelvas a cobrar.')
    } catch (failure) {
      if (!mounted.current) return
      setError(failure)
      const definite = failure instanceof ApiError && !failure.outcomeUnknown
      if (definite && (failure.status === 401 || failure.status === 403)) setDenied(true)
      const completed = await reconcile()
      if (!mounted.current) return
      if (!completed && definite) draft.markCheckout(false)
      if (!completed && !definite) setNotice('Resultado incierto. Incluso si una lectura aún muestra borrador, la petición puede seguir en curso. Solo consulta el mismo registro; no repitas el envío.')
    } finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  async function consult() {
    if (running.current) return
    running.current = true; setBusy(true)
    try { await reconcile() } finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  return { busy, error, notice, ticket, confirm, consult, denied }
}
