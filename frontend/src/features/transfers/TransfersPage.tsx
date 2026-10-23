import { useCallback, useMemo, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { Button } from '../../shared/ui/Button'
import { Notice } from '../../shared/ui/Notice'
import { PageControls } from '../../shared/ui/PageControls'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Panel } from '../../shared/ui/Panel'
import { FilterBar } from '../../shared/ui/PageToolbar'
import { DateText } from '../../shared/ui/DateText'
import { Badge } from '../../shared/ui/Badge'
import { equalId, states, stateLabel, transfersApi } from './api'
import type { DispatchSelection, GoodsAction, ReceiptSelection, Transfer, TransferState } from './api'
import { goodsSettled, goodsSuperseded } from './model'
import { useTrackedTransfer } from './useTrackedTransfer'
import { TransferRecovery } from './TransferRecovery'
import { RequestGoods } from './RequestGoods'
import { GoodsDetail } from './GoodsDetail'
import '../../shared/styles/operations.css'

export function TransfersPage() {
  const w = useWorkspace()
  return <TransfersWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function TransfersWorkspace() {
  const w = useWorkspace(); const { http, service } = useRuntime(); const client = useQueryClient()
  const api = useMemo(() => http ? transfersApi(http) : null, [http])
  const [branch, setBranch] = useState(w.branchId); const [status, setStatus] = useState<TransferState | ''>(''); const [offset, setOffset] = useState(0)
  const [requesting, setRequesting] = useState(false); const [requestEpoch, setRequestEpoch] = useState(0)
  const read = useCallback((id: string, signal: AbortSignal) => { if (!api) throw new ApiError(0, 'client.https_required'); return api.read(w, id, signal) }, [api, w])
  const verified = useCallback((value: Transfer) => {
    void client.invalidateQueries({ predicate: query => query.queryKey[0] === 'tenant' && query.queryKey[1] === w.tenantId && query.queryKey[3] === 'transfers' })
    if (value.status === 'in_transit' || value.status === 'received') void client.invalidateQueries({ predicate: query => query.queryKey[0] === 'tenant' && query.queryKey[1] === w.tenantId && [value.sourceBranchId, value.destinationBranchId].includes(String(query.queryKey[2])) && ['stock', 'inventory-lots', 'inventory-expiring', 'product-search'].includes(String(query.queryKey[3])) })
  }, [client, w.tenantId])
  const tracked = useTrackedTransfer('goods', read, goodsSettled, verified, goodsSuperseded)
  const branches = useQuery({ queryKey: scopedKey(w, 'transfer-branches', w.session.userId, w.access.membershipId), queryFn: ({ signal }) => service.branches(w.tenantId, signal), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const complete = branches.isSuccess && !branches.isError && branches.data.length < 100
  const list = useQuery({ queryKey: scopedKey(w, 'transfers', w.session.userId, w.access.membershipId, branch, status, offset), queryFn: ({ signal }) => { if (!api) throw new ApiError(0, 'client.https_required'); return api.list(w, w.access.role === 'owner' ? branch || undefined : w.branchId, status || undefined, offset, signal) }, enabled: Boolean(api), retry: false, staleTime: 0, refetchOnWindowFocus: false })
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
  function startNew() { tracked.startNew(); setRequesting(true); setRequestEpoch(value => value + 1) }
  return <div className="operations"><PageHeader title="Transferencias de mercadería" description={`Solicitudes, despacho y recepción · ${w.branch.name}`} action={<Button disabled={tracked.locked || requesting && !tracked.record} onClick={startNew}>Nueva transferencia</Button>} />
    <Link className="text-link" to={w.base + '/transferencias/sencillo'}>Transferir sencillo entre cajas</Link>
    {!http && <Notice tone="warning">Se requiere conexión HTTPS real.</Notice>}
    {tracked.error != null && <ApiErrorState error={tracked.error} title="No pudimos completar o consultar la transferencia" />}{tracked.notice && <p role="status">{tracked.notice}</p>}
    <Panel><div className="operation-body"><details open={Boolean(tracked.pending)} onToggle={event => { if (tracked.pending && !event.currentTarget.open) event.currentTarget.open = true }}><summary>Consultar o recuperar una transferencia</summary><TransferRecovery {...tracked} observed={Boolean(tracked.record)} /></details></div></Panel>
    <Panel><div className="operation-body"><h2>Solicitudes enviadas y recibidas</h2>
      <FilterBar><label htmlFor="transfer-branch">Sucursal de consulta</label><select id="transfer-branch" value={w.access.role === 'owner' ? branch : w.branchId} disabled={w.access.role !== 'owner' || tracked.busy} onChange={event => { setBranch(event.target.value); setOffset(0) }}>
        {w.access.role === 'owner' && <option value="">Todas las sucursales autorizadas</option>}<option value={w.branchId}>{w.branch.name} · actual</option>{!branches.isError && branches.data?.filter(row => !equalId(row.id, w.branchId)).map(row => <option key={row.id} value={row.id}>{row.name}</option>)}
      </select><label htmlFor="transfer-state">Estado de transferencia</label><select id="transfer-state" value={status} onChange={event => { setStatus(event.target.value as TransferState | ''); setOffset(0) }}><option value="">Todos los estados</option>{states.map(state => <option key={state} value={state}>{stateLabel[state]}</option>)}</select></FilterBar>
      {list.isFetching && <p role="status">Consultando transferencias…</p>}{list.isError && <ApiErrorState error={list.error} title="No pudimos consultar transferencias" />}
      {list.data && !list.isError && <div className="operation-table"><table><caption>Transferencias del filtro consultado</caption><thead><tr><th>Origen → destino</th><th>Relación con sede actual</th><th>Estado</th><th>Solicitud</th><th>Acción</th></tr></thead><tbody>{list.data.map(row => <tr key={row.id}><td>{row.sourceBranchName} → {row.destinationBranchName}</td><td>{equalId(row.sourceBranchId, w.branchId) ? 'Enviada' : equalId(row.destinationBranchId, w.branchId) ? 'Recibida' : 'Otra sucursal'}</td><td><Badge tone={row.status === 'received' ? 'success' : 'neutral'}>{stateLabel[row.status]}</Badge></td><td><DateText value={row.requestedAt} /></td><td><Button variant="quiet" disabled={tracked.busy || Boolean(tracked.pending && !tracked.unidentified && !equalId(tracked.id, row.id))} onClick={() => void tracked.consult(row.id)}>Ver transferencia {row.sourceBranchName} → {row.destinationBranchName}</Button></td></tr>)}</tbody></table>{!list.data.length && <p>No hay transferencias en esta página y filtro.</p>}</div>}
      <PageControls offset={offset} count={list.isError ? undefined : list.data?.length} busy={list.isFetching || tracked.busy} change={setOffset} refresh={() => void list.refetch()} />
      {branches.isError && <ApiErrorState error={branches.error} title="No pudimos consultar sucursales" retry={() => void branches.refetch()} />}
    </div></Panel>
    {tracked.record && <Panel><div className="operation-body"><GoodsDetail key={JSON.stringify(tracked.record)} transfer={tracked.record} locked={tracked.locked} transition={transition} /></div></Panel>}
    {requesting && <Panel hidden={Boolean(tracked.record)}><div className="operation-body"><RequestGoods key={requestEpoch} branches={complete ? branches.data : []} destinationId={w.branchId} destinationName={w.branch.name} disabled={!api || !complete || tracked.locked} create={(source, lines) => { if (api && complete) void tracked.run('create', signal => api.create(w, source, w.branchId, lines, signal)) }} /></div></Panel>}
  </div>
}
