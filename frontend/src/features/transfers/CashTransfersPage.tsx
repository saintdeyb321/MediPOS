import { useCallback, useMemo, useState } from 'react'
import { ArrowLeft, Banknote } from 'lucide-react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { compare, decimalInput, decimalLabel, maximumMoney } from '../../shared/api/decimal'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Notice } from '../../shared/ui/Notice'
import { Panel } from '../../shared/ui/Panel'
import { PageControls } from '../../shared/ui/PageControls'
import { DateText } from '../../shared/ui/DateText'
import { DataSection } from '../../shared/ui/DataSection'
import { FilterBar } from '../../shared/ui/PageToolbar'
import { EditorLayout } from '../../shared/ui/EditorLayout'
import { useNavigationProtection } from '../../shared/ui/NavigationProtection'
import { equalId } from './api'
import { cashSettled, cashTransfersApi } from './cashApi'
import type { CashTransfer } from './cashApi'
import { useTrackedTransfer } from './useTrackedTransfer'
import { TransferRecovery } from './TransferRecovery'
import { useTransferCash } from './useTransferCash'
import { TransferTabs } from './TransferTabs'
import { CashSelection } from './CashSelection'
import '../../shared/styles/operations.css'

export function CashTransfersPage() {
  const w = useWorkspace()
  if (w.access.role !== 'owner' && w.access.role !== 'cashier') return <Notice tone="warning">Sencillo requiere Owner o Cashier con acceso operacional vigente. Pharmacist no tiene este permiso.</Notice>
  return <CashTransfersWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function CashTransfersWorkspace() {
  const w = useWorkspace(); const { http, service } = useRuntime(); const client = useQueryClient()
  const api = useMemo(() => http ? cashTransfersApi(http) : null, [http])
  const [destination, setDestination] = useState(''); const [amountText, setAmountText] = useState('')
  const [filter, setFilter] = useState(w.branchId); const [offset, setOffset] = useState(0)
  const [directory, setDirectory] = useState(false)
  const [cancelDispatch, setCancelDispatch] = useState(false)
  const [dispatchOpen, setDispatchOpen] = useState(false)
  const read = useCallback((id: string, signal: AbortSignal) => { if (!api) throw new ApiError(0, 'client.https_required'); return api.read(w, id, signal) }, [api, w])
  const verified = useCallback((value: CashTransfer) => {
    void client.invalidateQueries({ predicate: query => query.queryKey[0] === 'tenant' && query.queryKey[1] === w.tenantId && ['cash-transfers', 'transfer-cash-status', 'transfer-cash-choices', 'current-cash', 'cash-status', 'active-cash'].includes(String(query.queryKey[3])) && (query.queryKey[3] === 'cash-transfers' || [value.sourceBranchId, value.destinationBranchId].includes(String(query.queryKey[2]))) })
  }, [client, w.tenantId])
  const tracked = useTrackedTransfer('cashTransfer', read, cashSettled, verified, undefined, {
    storageKey: `medipos-cash-dispatch/${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`,
    recover: (requestId, signal) => {
      if (!api) throw new ApiError(0, 'client.https_required')
      return api.recoverDispatch(w, requestId, w.session.userId, signal)
    },
  })
  const t = tracked.record
  const editor = !directory && Boolean(dispatchOpen || tracked.id || t || tracked.keyedCreation)
  const cash = useTransferCash(editor && Boolean(dispatchOpen || t))
  useNavigationProtection({ busy: tracked.busy, pending: Boolean(tracked.pending) })
  const branches = useQuery({ queryKey: scopedKey(w, 'transfer-branches', w.session.userId, w.access.membershipId), queryFn: ({ signal }) => service.branches(w.tenantId, signal), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const pending = useQuery({ queryKey: scopedKey(w, 'cash-transfers', w.session.userId, w.access.membershipId, filter, offset), queryFn: ({ signal }) => { if (!api) throw new ApiError(0, 'client.https_required'); return api.pending(w, w.access.role === 'owner' ? filter || undefined : w.branchId, offset, signal) }, enabled: Boolean(api && (!editor || tracked.unidentified)), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const complete = branches.isSuccess && !branches.isError && branches.data.length < 100
  const label = (id: string) => branches.data?.find(row => equalId(row.id, id))?.name ?? 'Sucursal no disponible en el directorio'
  const amount = decimalInput(amountText, maximumMoney, 4)
  const canDispatch = Boolean(!tracked.locked && !tracked.record && api && complete && branches.data.some(row => equalId(row.id, destination) && !equalId(row.id, w.branchId)) && cash.session && amount && cash.session.expectedCashAmount !== null && compare(amount, cash.session.expectedCashAmount) <= 0)
  const canReceive = Boolean(t?.status === 'in_transit' && equalId(t.destinationBranchId, w.branchId) && cash.session && !equalId(cash.session.cashSessionId, t.sourceCashSessionId))
  const cashLabel = cash.choices.data?.find(row => equalId(row.id, cash.session?.cashSessionId ?? ''))?.label
  const resume = directory && dispatchOpen && !tracked.id && !t
  return <div className="operations">
    {editor && <Button variant="quiet" disabled={tracked.busy || Boolean(tracked.pending)} onClick={() => setDirectory(true)}><ArrowLeft size={16} aria-hidden="true" />Volver a transferencias</Button>}
    <PageHeader icon={Banknote} eyebrow="" focusOnChange title={editor ? tracked.pending ? 'Recuperar transferencia' : t || tracked.id ? 'Transferencia de efectivo' : 'Despachar sencillo' : 'Transferencias'}
      action={!editor && <Button disabled={tracked.locked} onClick={() => {
        setDirectory(false)
        if (!resume) { tracked.startNew(); setDispatchOpen(true); setAmountText(''); setDestination(''); cash.setId('') }
      }}>{resume ? 'Retomar envío de sencillo' : 'Nuevo envío de sencillo'}</Button>} />
    {tracked.error != null && <ApiErrorState error={tracked.error} title="No pudimos completar o consultar el sencillo" />}{tracked.notice && <p role="status">{tracked.notice}</p>}
    {tracked.pending && <Notice tone="warning">No pudimos confirmar el resultado. Consulta el envío original.</Notice>}
    <div hidden={editor && !tracked.unidentified}><DataSection tabs={<TransferTabs cash />}
      toolbar={<FilterBar>{w.access.role === 'owner' ? <label>Destino de consulta<select id="cash-transfer-destination" value={w.access.role === 'owner' ? filter : w.branchId} disabled={w.access.role !== 'owner' || tracked.busy} onChange={event => { setFilter(event.target.value); setOffset(0) }}>
      {w.access.role === 'owner' && <option value="">Todos los destinos autorizados</option>}<option value={w.branchId}>{w.branch.name} · actual</option>{!branches.isError && branches.data?.filter(row => !equalId(row.id, w.branchId)).map(row => <option key={row.id} value={row.id}>{row.name}</option>)}</select></label> : <span>Sencillo pendiente de recepción</span>}</FilterBar>}
      footer={<PageControls offset={offset} count={!pending.isError && pending.data?.hasMore ? 20 : 0} busy={pending.isFetching || tracked.busy} change={setOffset} />}>
      {pending.isFetching && <p>Consultando sencillo pendiente…</p>}{pending.isError && <ApiErrorState error={pending.error} title="No pudimos consultar sencillo pendiente" retry={() => void pending.refetch()} />}
      {branches.isError && <ApiErrorState error={branches.error} title="No pudimos consultar sucursales" retry={() => void branches.refetch()} />}
      {pending.data && !pending.isError && <div className="operation-table" role="region" aria-label="Listado de sencillo pendiente" tabIndex={0}><table><thead><tr><th>Origen → destino</th><th className="table-number">Importe PEN</th><th>Despacho</th><th scope="col" className="table-action">Acción</th></tr></thead><tbody>{pending.data.items.map(row => <tr key={row.id}><td>{label(row.sourceBranchId)} → {label(row.destinationBranchId)}</td><td className="table-number">{decimalLabel(row.amount, 2)}</td><td><DateText value={row.dispatchedAt} /></td><td className="table-action"><Button variant="quiet" disabled={tracked.busy || (dispatchOpen && !t && !tracked.pending) || tracked.keyedCreation || Boolean(tracked.pending && !tracked.unidentified && !equalId(tracked.id, row.id))} aria-label={`Consultar sencillo de ${label(row.sourceBranchId)}`} onClick={() => { setDirectory(false); setDispatchOpen(false); void tracked.consult(row.id) }}>Ver</Button></td></tr>)}</tbody></table>{!pending.data.items.length && <p>No hay sencillo pendiente en esta página y filtro.</p>}</div>}
    </DataSection></div>
    <div hidden={(editor || resume) && !tracked.pending && !tracked.error}><details className="transfer-support" open={Boolean(tracked.pending)}
      onToggle={event => { if (tracked.pending && !event.currentTarget.open) event.currentTarget.open = true }}>
      <summary>Consultar por código de soporte</summary>
      <TransferRecovery {...tracked} observed={Boolean(t)} consult={id => { setDirectory(false); return tracked.consult(id) }} />
      {tracked.unidentified && <p>Una transferencia ya recibida no figura en pendientes. Si eres cajero y no puedes localizarla, consulta con el propietario antes de repetir el despacho.</p>}
    </details></div>
    <div hidden={!editor}>
      <EditorLayout summary={(dispatchOpen || t) && <Panel><div className="operation-body"><CashSelection cash={cash} disabled={tracked.locked} /></div></Panel>}>
    {t && <Panel><div className="operation-body"><h2>Sencillo: {t.status === 'received' ? 'RECIBIDO' : 'EN TRÁNSITO'}</h2><p>{label(t.sourceBranchId)} → {label(t.destinationBranchId)} · S/ {decimalLabel(t.amount, 2)}</p><p>Despacho UTC: {t.dispatchedAt} · {equalId(t.dispatchedByActorId, w.session.userId) ? 'Tu usuario' : 'Otro operador registrado'}</p>
      {t.status === 'received' ? <p>Recepción UTC: {t.receivedAt} · {t.receivedByActorId && equalId(t.receivedByActorId, w.session.userId) ? 'Tu usuario' : 'Otro operador registrado'}. Estado terminal; no se reenvía.</p> : <fieldset disabled={tracked.locked || !canReceive}><legend>Recepción completa en la caja elegida</legend><p>Recibe físicamente S/ {decimalLabel(t.amount)} de {label(t.sourceBranchId)} en {cashLabel ?? 'la caja destino que selecciones'} de {w.branch.name}. La recepción es por el importe completo.</p><Button onClick={() => { if (api && cash.session && canReceive) void tracked.run('receive', signal => api.receive(w, t.id, cash.session!.cashSessionId, signal)) }}>Confirmar recepción de sencillo</Button>
        {!canReceive && <p>Selecciona una caja abierta autorizada en la sucursal destino. Si estás en otra sede, cambia de sucursal y recupera esta transferencia; no se presume autorización remota.</p>}</fieldset>}
    </div></Panel>}
    <Panel hidden={!dispatchOpen || Boolean(t)}><div className="operation-body"><h2>Despachar sencillo desde {w.branch.name}</h2><fieldset disabled={tracked.locked || !api || !complete}><legend>Destino e importe</legend><label htmlFor="new-cash-transfer-destination">Sucursal destino de sencillo</label><select id="new-cash-transfer-destination" value={destination} onChange={event => { setDestination(event.target.value) }}><option value="">Seleccionar otra sucursal consultable</option>{complete && branches.data.filter(row => !equalId(row.id, w.branchId)).map(row => <option key={row.id} value={row.id}>{row.name}</option>)}</select>
      <TextField label="Importe de sencillo PEN" inputMode="decimal" value={amountText} maxLength={60} onChange={event => { setAmountText(event.target.value) }} hint="Hasta el efectivo esperado de la caja seleccionada." />
      {canDispatch && amount && <p>Entrega S/ {decimalLabel(amount)} de {cashLabel ?? 'la caja seleccionada'} de {w.branch.name} a {label(destination)}. El efectivo quedará en tránsito hasta su recepción.</p>}<Button disabled={!canDispatch} onClick={() => { if (api && amount && cash.session && canDispatch) void tracked.run('create', (signal, requestId) => api.dispatch(w, cash.session!.cashSessionId, destination, amount, requestId, signal)) }}>Confirmar despacho de sencillo</Button>
      {!canDispatch && <p>{cash.session?.expectedCashAmount === null
        ? 'La consulta de esta caja abierta no informa el saldo esperado. El despacho queda bloqueado.'
        : 'Selecciona otra sede y una caja abierta con efectivo esperado suficiente.'}</p>}</fieldset>
      {!tracked.pending && <Button variant="quiet" disabled={tracked.busy} onClick={() => {
        if (amountText || destination || cash.id) setCancelDispatch(true)
        else { setDirectory(true); setDispatchOpen(false) }
      }}>Descartar envío sin guardar</Button>}
    </div></Panel>
    <ConfirmDialog open={cancelDispatch} title="Descartar envío sin guardar" confirmLabel="Descartar" disabled={tracked.busy || Boolean(tracked.pending)} onClose={() => setCancelDispatch(false)}
      onConfirm={() => { setCancelDispatch(false); setDispatchOpen(false); setAmountText(''); setDestination(''); cash.setId(''); setDirectory(true) }}>
      <p>Se perderán los datos locales de este envío.</p>
    </ConfirmDialog>
    {t && <Button variant="secondary" disabled={tracked.locked} onClick={() => { tracked.startNew(); setDispatchOpen(true); setAmountText(''); setDestination(''); cash.setId('') }}>Preparar otro despacho de sencillo</Button>}
      </EditorLayout>
    </div>
  </div>
}
