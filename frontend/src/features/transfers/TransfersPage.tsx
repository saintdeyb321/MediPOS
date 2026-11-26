import { useCallback, useMemo, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, Truck } from 'lucide-react'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { Button } from '../../shared/ui/Button'
import { Notice } from '../../shared/ui/Notice'
import { PageControls } from '../../shared/ui/PageControls'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Panel } from '../../shared/ui/Panel'
import { DataSection } from '../../shared/ui/DataSection'
import { FilterBar } from '../../shared/ui/PageToolbar'
import { DateText } from '../../shared/ui/DateText'
import { Badge } from '../../shared/ui/Badge'
import { equalId, states, stateLabel, transfersApi } from './api'
import type { DispatchSelection, GoodsAction, ReceiptSelection, Transfer, TransferState } from './api'
import { goodsSettled, goodsSuperseded } from './model'
import { useTrackedTransfer } from './useTrackedTransfer'
import { TransferRecovery } from './TransferRecovery'
import { RequestGoods } from './RequestGoods'
import { TransferTabs } from './TransferTabs'
import { GoodsDetail } from './GoodsDetail'
import { useNavigationProtection } from '../../shared/ui/NavigationProtection'
import '../../shared/styles/operations.css'

export function TransfersPage() {
  const w = useWorkspace()
  return <TransfersWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function TransfersWorkspace() {
  const w = useWorkspace(); const { http, service } = useRuntime(); const client = useQueryClient()
  const api = useMemo(() => http ? transfersApi(http) : null, [http])
  const [branch, setBranch] = useState(w.branchId); const [status, setStatus] = useState<TransferState | ''>(''); const [offset, setOffset] = useState(0)
  const [directory, setDirectory] = useState(false)
  const [cancelRequest, setCancelRequest] = useState(false)
  const [requesting, setRequesting] = useState(false); const [requestEpoch, setRequestEpoch] = useState(0)
  const read = useCallback((id: string, signal: AbortSignal) => { if (!api) throw new ApiError(0, 'client.https_required'); return api.read(w, id, signal) }, [api, w])
  const verified = useCallback((value: Transfer) => {
    void client.invalidateQueries({ predicate: query => query.queryKey[0] === 'tenant' && query.queryKey[1] === w.tenantId && query.queryKey[3] === 'transfers' })
    if (value.status === 'in_transit' || value.status === 'received') void client.invalidateQueries({ predicate: query => query.queryKey[0] === 'tenant' && query.queryKey[1] === w.tenantId && [value.sourceBranchId, value.destinationBranchId].includes(String(query.queryKey[2])) && ['stock', 'inventory-lots', 'inventory-expiring', 'product-search'].includes(String(query.queryKey[3])) })
  }, [client, w.tenantId])
  const tracked = useTrackedTransfer('goods', read, goodsSettled, verified, goodsSuperseded, {
    storageKey: `medipos-goods-request/${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`,
    recover: (requestId, signal) => {
      if (!api) throw new ApiError(0, 'client.https_required')
      return api.recoverCreation(w, requestId, w.session.userId, signal)
    },
  })
  const editor = !directory && Boolean(requesting || tracked.record || tracked.id || tracked.keyedCreation)
  useNavigationProtection({ busy: tracked.busy, pending: Boolean(tracked.pending) })
  const branches = useQuery({ queryKey: scopedKey(w, 'transfer-branches', w.session.userId, w.access.membershipId), queryFn: ({ signal }) => service.branches(w.tenantId, signal), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const complete = branches.isSuccess && !branches.isError && branches.data.length < 100
  const list = useQuery({ queryKey: scopedKey(w, 'transfers', w.session.userId, w.access.membershipId, branch, status, offset), queryFn: ({ signal }) => { if (!api) throw new ApiError(0, 'client.https_required'); return api.list(w, w.access.role === 'owner' ? branch || undefined : w.branchId, status || undefined, offset, signal) }, enabled: Boolean(api && (!editor || tracked.unidentified)), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  function transition(action: Exclude<GoodsAction, 'create'>, input?: string | DispatchSelection[] | ReceiptSelection[]) {
    const t = tracked.record; if (!api || !t || tracked.locked) return
    void tracked.run(action, signal => {
      if (action === 'approve') return api.approve(w, t.id, signal)
      if (action === 'cancel' && typeof input === 'string') return api.cancel(w, t.id, input, signal)
      if (action === 'dispatch' && Array.isArray(input) && input.every(row => 'transferLineId' in row)) return api.dispatch(w, t.id, input as DispatchSelection[], signal)
      if (action === 'receive' && Array.isArray(input) && input.every(row => 'allocationId' in row)) return api.receive(w, t.id, input as ReceiptSelection[], signal)
      throw new ApiError(400, 'client.invalid_transfer')
    })
  }
  function startNew() { tracked.startNew(); setDirectory(false); setRequesting(true); setRequestEpoch(value => value + 1) }
  const resume = directory && requesting && !tracked.record && !tracked.id
  return <div className="operations">
    {editor && <Button variant="quiet" disabled={tracked.busy || Boolean(tracked.pending)} onClick={() => setDirectory(true)}><ArrowLeft size={16} aria-hidden="true" />Volver a transferencias</Button>}
    <PageHeader icon={Truck} eyebrow="" focusOnChange title={editor ? tracked.pending ? 'Recuperar transferencia' : tracked.id ? 'Transferencia de mercadería' : 'Nueva transferencia' : 'Transferencias'}
      action={!editor && <Button disabled={tracked.locked} onClick={resume ? () => setDirectory(false) : startNew}>{resume ? 'Retomar transferencia' : 'Nueva transferencia'}</Button>} />
    {!http && <Notice tone="warning">Se requiere conexión HTTPS real.</Notice>}
    {tracked.error != null && <ApiErrorState error={tracked.error} title="No pudimos completar o consultar la transferencia" />}{tracked.notice && <p role="status">{tracked.notice}</p>}
    {tracked.pending && <Notice tone="warning">No pudimos confirmar el resultado. Consulta la transferencia original.</Notice>}
    <div hidden={editor && !tracked.unidentified}><DataSection tabs={<TransferTabs />}
      toolbar={<FilterBar>{w.access.role === 'owner' && <label>Sucursal de consulta<select id="transfer-branch" value={w.access.role === 'owner' ? branch : w.branchId} disabled={w.access.role !== 'owner' || tracked.busy} onChange={event => { setBranch(event.target.value); setOffset(0) }}>
        {w.access.role === 'owner' && <option value="">Todas las sucursales autorizadas</option>}<option value={w.branchId}>{w.branch.name} · actual</option>{!branches.isError && branches.data?.filter(row => !equalId(row.id, w.branchId)).map(row => <option key={row.id} value={row.id}>{row.name}</option>)}
      </select></label>}<label>Estado de transferencia<select id="transfer-state" value={status} onChange={event => { setStatus(event.target.value as TransferState | ''); setOffset(0) }}><option value="">Todos los estados</option>{states.map(state => <option key={state} value={state}>{stateLabel[state]}</option>)}</select></label></FilterBar>}
      footer={<PageControls offset={offset} count={list.isError ? undefined : list.data?.length} busy={list.isFetching || tracked.busy} change={setOffset} />}>
      {list.isFetching && <p role="status">Consultando transferencias…</p>}{list.isError && <ApiErrorState error={list.error} title="No pudimos consultar transferencias" retry={() => void list.refetch()} />}
      {list.data && !list.isError && <div className="operation-table" role="region" aria-label="Listado de transferencias" tabIndex={0}><table><thead><tr><th>Origen → destino</th><th>Relación con sede actual</th><th>Estado</th><th>Solicitud</th><th scope="col" className="table-action">Acción</th></tr></thead><tbody>{list.data.map(row => <tr key={row.id}><td>{row.sourceBranchName} → {row.destinationBranchName}</td><td>{equalId(row.sourceBranchId, w.branchId) ? 'Enviada' : equalId(row.destinationBranchId, w.branchId) ? 'Recibida' : 'Otra sucursal'}</td><td><Badge tone={row.status === 'received' ? 'success' : 'neutral'}>{stateLabel[row.status]}</Badge></td><td><DateText value={row.requestedAt} /></td><td className="table-action"><Button variant="quiet" disabled={tracked.busy || (requesting && !tracked.record && !tracked.pending) || tracked.keyedCreation || Boolean(tracked.pending && !tracked.unidentified && !equalId(tracked.id, row.id))} aria-label={`Ver transferencia ${row.sourceBranchName} → ${row.destinationBranchName}`} onClick={() => { setDirectory(false); setRequesting(false); void tracked.consult(row.id) }}>Ver</Button></td></tr>)}</tbody></table>{!list.data.length && <p>No hay transferencias en esta página y filtro.</p>}</div>}
      {branches.isError && <ApiErrorState error={branches.error} title="No pudimos consultar sucursales" retry={() => void branches.refetch()} />}
    </DataSection></div>
    <div hidden={(editor || resume) && !tracked.pending && !tracked.error}><details className="transfer-support" open={Boolean(tracked.pending)} onToggle={event => { if (tracked.pending && !event.currentTarget.open) event.currentTarget.open = true }}><summary>Consultar por código de soporte</summary><TransferRecovery {...tracked} observed={Boolean(tracked.record)} consult={id => { setDirectory(false); return tracked.consult(id) }} /></details></div>
    {tracked.record && <Panel hidden={!editor}><div className="operation-body"><GoodsDetail key={JSON.stringify(tracked.record)} transfer={tracked.record} locked={tracked.locked} transition={transition} /></div></Panel>}
    {requesting && !tracked.record && complete && !branches.data.some(row => !equalId(row.id, w.branchId)) && editor && <Notice tone="info">No hay otra sucursal disponible para solicitar mercadería.</Notice>}
    {requesting && <Panel hidden={!editor || Boolean(tracked.record)}><div className="operation-body"><RequestGoods key={requestEpoch} branches={complete ? branches.data : []} destinationId={w.branchId} destinationName={w.branch.name} disabled={!api || !complete || tracked.locked} create={(source, lines) => { if (api && complete) void tracked.run('create', (signal, requestId) => api.create(w, source, w.branchId, lines, requestId, signal)) }}
      discard={!tracked.busy && !tracked.pending ? dirty => {
        if (dirty) setCancelRequest(true)
        else { setRequesting(false); setDirectory(true) }
      } : undefined} />
    </div></Panel>}
    <ConfirmDialog open={cancelRequest} title="Descartar solicitud sin guardar" confirmLabel="Descartar" disabled={tracked.busy || Boolean(tracked.pending)} onClose={() => setCancelRequest(false)}
      onConfirm={() => { setCancelRequest(false); setRequesting(false); setDirectory(true) }}>
      <p>Se perderán los datos locales de esta solicitud.</p>
    </ConfirmDialog>
  </div>
}
