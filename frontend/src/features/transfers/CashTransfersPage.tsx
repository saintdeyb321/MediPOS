import { useCallback, useMemo, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { compare, decimalInput, decimalLabel, maximumMoney } from '../../shared/api/decimal'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Notice } from '../../shared/ui/Notice'
import { Panel } from '../../shared/ui/Panel'
import { PageControls } from '../../shared/ui/PageControls'
import { DateText } from '../../shared/ui/DateText'
import { Steps } from '../../shared/ui/Steps'
import { equalId } from './api'
import { cashSettled, cashTransfersApi } from './cashApi'
import type { CashTransfer } from './cashApi'
import { useTrackedTransfer } from './useTrackedTransfer'
import { TransferRecovery } from './TransferRecovery'
import { useTransferCash } from './useTransferCash'
import { CashSelection } from './CashSelection'
import '../../shared/styles/operations.css'

export function CashTransfersPage() {
  const w = useWorkspace()
  if (w.access.role !== 'owner' && w.access.role !== 'cashier') return <Notice tone="warning">Sencillo requiere Owner o Cashier con acceso operacional vigente. Pharmacist no tiene este permiso.</Notice>
  return <CashTransfersWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function CashTransfersWorkspace() {
  const w = useWorkspace(); const { http, service } = useRuntime(); const client = useQueryClient(); const cash = useTransferCash()
  const api = useMemo(() => http ? cashTransfersApi(http) : null, [http])
  const [destination, setDestination] = useState(''); const [amountText, setAmountText] = useState(''); const [reviewed, setReviewed] = useState(''); const [receiptReviewed, setReceiptReviewed] = useState('')
  const [filter, setFilter] = useState(w.branchId); const [offset, setOffset] = useState(0)
  const [dispatchOpen, setDispatchOpen] = useState(false)
  const read = useCallback((id: string, signal: AbortSignal) => { if (!api) throw new ApiError(0, 'client.https_required'); return api.read(w, id, signal) }, [api, w])
  const verified = useCallback((value: CashTransfer) => {
    void client.invalidateQueries({ predicate: query => query.queryKey[0] === 'tenant' && query.queryKey[1] === w.tenantId && ['cash-transfers', 'transfer-cash-status', 'transfer-cash-choices', 'current-cash', 'cash-status', 'active-cash'].includes(String(query.queryKey[3])) && (query.queryKey[3] === 'cash-transfers' || [value.sourceBranchId, value.destinationBranchId].includes(String(query.queryKey[2]))) })
  }, [client, w.tenantId])
  const tracked = useTrackedTransfer('cashTransfer', read, cashSettled, verified)
  const branches = useQuery({ queryKey: scopedKey(w, 'transfer-branches', w.session.userId, w.access.membershipId), queryFn: ({ signal }) => service.branches(w.tenantId, signal), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const pending = useQuery({ queryKey: scopedKey(w, 'cash-transfers', w.session.userId, w.access.membershipId, filter, offset), queryFn: ({ signal }) => { if (!api) throw new ApiError(0, 'client.https_required'); return api.pending(w, w.access.role === 'owner' ? filter || undefined : w.branchId, offset, signal) }, enabled: Boolean(api), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const complete = branches.isSuccess && !branches.isError && branches.data.length < 100
  const label = (id: string) => branches.data?.find(row => equalId(row.id, id))?.name ?? 'Sucursal no disponible en el directorio'
  const amount = decimalInput(amountText, maximumMoney, 4)
  const canDispatch = Boolean(!tracked.locked && !tracked.record && api && complete && branches.data.some(row => equalId(row.id, destination) && !equalId(row.id, w.branchId)) && cash.session && amount && cash.session.expectedCashAmount !== null && compare(amount, cash.session.expectedCashAmount) <= 0)
  const t = tracked.record
  const canReceive = Boolean(t?.status === 'in_transit' && equalId(t.destinationBranchId, w.branchId) && cash.session && !equalId(cash.session.cashSessionId, t.sourceCashSessionId))
  const dispatchReview = JSON.stringify([destination, amount?.literal, cash.session?.cashSessionId])
  const receiptReview = JSON.stringify([t?.id, t?.amount.literal, t?.destinationBranchId, cash.session?.cashSessionId])
  return <div className="operations"><PageHeader title="Transferencias de sencillo" description={`Efectivo entre cajas · ${w.branch.name}`} action={<Button disabled={tracked.locked || Boolean(t) || dispatchOpen} onClick={() => setDispatchOpen(true)}>Nuevo envío de sencillo</Button>} />
    {(dispatchOpen || t) && <Steps labels={['Caja y destino', 'Revisar despacho', 'Recibir efectivo']} current={t ? 2 : reviewed === dispatchReview && amount ? 1 : 0} label="Transferencia de sencillo" />}
    {tracked.error != null && <ApiErrorState error={tracked.error} title="No pudimos completar o consultar el sencillo" />}{tracked.notice && <p role="status">{tracked.notice}</p>}
    <Panel><div className="operation-body"><details open={Boolean(tracked.pending)} onToggle={event => { if (tracked.pending && !event.currentTarget.open) event.currentTarget.open = true }}><summary>Consultar o recuperar un envío</summary><TransferRecovery {...tracked} observed={Boolean(t)} /><p>Sin identificador confirmado, Owner puede consultar pendientes de todas las sedes. Cashier no dispone de un listado general de envíos: debe conciliar con Owner. Una transferencia ya recibida no figura en pendientes; no se repite el despacho por su ausencia.</p></details></div></Panel>
    <Panel><div className="operation-body"><h2>Sencillo pendiente de recepción</h2><label htmlFor="cash-transfer-destination">Destino de consulta</label><select id="cash-transfer-destination" value={w.access.role === 'owner' ? filter : w.branchId} disabled={w.access.role !== 'owner' || tracked.busy} onChange={event => { setFilter(event.target.value); setOffset(0) }}>
      {w.access.role === 'owner' && <option value="">Todos los destinos autorizados</option>}<option value={w.branchId}>{w.branch.name} · actual</option>{!branches.isError && branches.data?.filter(row => !equalId(row.id, w.branchId)).map(row => <option key={row.id} value={row.id}>{row.name}</option>)}</select>
      {pending.isFetching && <p>Consultando sencillo pendiente…</p>}{pending.isError && <ApiErrorState error={pending.error} title="No pudimos consultar sencillo pendiente" />}
      {branches.isError && <ApiErrorState error={branches.error} title="No pudimos consultar sucursales" retry={() => void branches.refetch()} />}
      {pending.data && !pending.isError && <div className="operation-table"><table><caption>Importes en tránsito del servidor</caption><thead><tr><th>Origen → destino</th><th>Importe PEN</th><th>Despacho UTC</th><th>Acción</th></tr></thead><tbody>{pending.data.items.map(row => <tr key={row.id}><td>{label(row.sourceBranchId)} → {label(row.destinationBranchId)}</td><td>{decimalLabel(row.amount, 2)}</td><td>{row.dispatchedAt}</td><td><Button variant="quiet" disabled={tracked.busy || Boolean(tracked.pending && !tracked.unidentified && !equalId(tracked.id, row.id))} onClick={() => { setReceiptReviewed(''); void tracked.consult(row.id) }}>Consultar sencillo de {label(row.sourceBranchId)}</Button></td></tr>)}</tbody></table>{!pending.data.items.length && <p>No hay sencillo pendiente en esta página y filtro.</p>}</div>}
      <PageControls offset={offset} count={!pending.isError && pending.data?.hasMore ? 20 : 0} busy={pending.isFetching || tracked.busy} change={setOffset} refresh={() => void pending.refetch()} />
    </div></Panel>
    <Panel hidden={!dispatchOpen && !t}><div className="operation-body"><CashSelection cash={cash} disabled={tracked.locked} /></div></Panel>
    {t && <Panel><div className="operation-body"><h2>Sencillo: {t.status === 'received' ? 'RECIBIDO' : 'EN TRÁNSITO'}</h2><p>{label(t.sourceBranchId)} → {label(t.destinationBranchId)} · S/ {decimalLabel(t.amount, 2)}</p><p>Despacho UTC: {t.dispatchedAt} · {equalId(t.dispatchedByActorId, w.session.userId) ? 'Tu usuario' : 'Otro operador registrado'}</p>
      {t.status === 'received' ? <p>Recepción UTC: {t.receivedAt} · {t.receivedByActorId && equalId(t.receivedByActorId, w.session.userId) ? 'Tu usuario' : 'Otro operador registrado'}. Estado terminal; no se reenvía.</p> : <fieldset disabled={tracked.locked || !canReceive}><legend>Recepción completa en la caja elegida</legend><p>Recibir exactamente S/ {decimalLabel(t.amount, 2)}. No hay recepción parcial ni ajuste local del ledger.</p><label><input type="checkbox" checked={receiptReviewed === receiptReview} onChange={event => setReceiptReviewed(event.target.checked ? receiptReview : '')} /> Comprobé el efectivo recibido completo y la caja destino abierta.</label><Button disabled={receiptReviewed !== receiptReview} onClick={() => { if (api && cash.session && canReceive) void tracked.run('receive', signal => api.receive(w, t.id, cash.session!.cashSessionId, signal)) }}>Confirmar recepción de sencillo</Button>
        {!canReceive && <p>Selecciona una caja abierta autorizada en la sucursal destino. Si estás en otra sede, cambia de sucursal y recupera esta transferencia; no se presume autorización remota.</p>}</fieldset>}
    </div></Panel>}
    <Panel hidden={!dispatchOpen || Boolean(t)}><div className="operation-body"><h2>Despachar sencillo desde {w.branch.name}</h2><fieldset disabled={tracked.locked || !api || !complete}><legend>Destino e importe</legend><label htmlFor="new-cash-transfer-destination">Sucursal destino de sencillo</label><select id="new-cash-transfer-destination" value={destination} onChange={event => { setDestination(event.target.value); setReviewed('') }}><option value="">Seleccionar otra sucursal consultable</option>{complete && branches.data.filter(row => !equalId(row.id, w.branchId)).map(row => <option key={row.id} value={row.id}>{row.name}</option>)}</select>
      <TextField label="Importe de sencillo PEN" inputMode="decimal" value={amountText} maxLength={60} onChange={event => { setAmountText(event.target.value); setReviewed('') }} /><p>Importe positivo numeric(18,4), hasta {maximumMoney.literal}; no superior al efectivo esperado consultado. El destino necesitará una caja abierta al recibir.</p>
      <label><input type="checkbox" checked={reviewed === dispatchReview} onChange={event => setReviewed(event.target.checked ? dispatchReview : '')} /> Revisé caja origen, sucursal destino e importe; autorizo este despacho una vez.</label><Button disabled={!canDispatch || reviewed !== dispatchReview} onClick={() => { if (api && amount && cash.session && canDispatch) void tracked.run('create', signal => api.dispatch(w, cash.session!.cashSessionId, destination, amount, signal)) }}>Confirmar despacho de sencillo</Button>
      {!canDispatch && <p>Requiere caja abierta, saldo suficiente consultado y otro destino disponible. Si tu directorio no incluye otra sede, no se admiten identificadores libres como alternativa.</p>}</fieldset></div></Panel>
    {t && <Button variant="secondary" disabled={tracked.locked} onClick={() => { tracked.startNew(); setDispatchOpen(true); setReviewed(''); setReceiptReviewed(''); setAmountText(''); setDestination(''); cash.setId('') }}>Preparar otro despacho de sencillo</Button>}
    {t && <p>Último despacho: <DateText value={t.dispatchedAt} /></p>}
  </div>
}
