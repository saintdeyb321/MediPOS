import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { ProductLabel } from '../catalog'
import { scopedKey } from '../../shared/api/contracts'
import { decimalLabel } from '../../shared/api/decimal'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import type { DispatchSelection, GoodsAction, ReceiptSelection, Transfer } from './api'
import { equalId, stateLabel } from './api'
import { goodsPermissions } from './model'
import { DispatchGoods } from './DispatchGoods'
import { ReceiveGoods } from './ReceiveGoods'

interface Props { transfer: Transfer; locked: boolean; transition: (action: Exclude<GoodsAction, 'create'>, input?: string | DispatchSelection[] | ReceiptSelection[]) => void }
export function GoodsDetail({ transfer: t, locked, transition }: Props) {
  const w = useWorkspace(); const { service } = useRuntime()
  const [approve, setApprove] = useState(false); const [cancel, setCancel] = useState(false); const [reason, setReason] = useState('')
  const source = useQuery({ queryKey: scopedKey({ ...w, branchId: t.sourceBranchId }, 'transfer-access', w.session.userId, w.access.membershipId), queryFn: ({ signal }) => service.access(w.tenantId, t.sourceBranchId, signal), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const destination = useQuery({ queryKey: scopedKey({ ...w, branchId: t.destinationBranchId }, 'transfer-access', w.session.userId, w.access.membershipId), queryFn: ({ signal }) => service.access(w.tenantId, t.destinationBranchId, signal), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const permissions = goodsPermissions(t, w, source.isError || source.isFetching ? undefined : source.data, destination.isError || destination.isFetching ? undefined : destination.data)
  const labels = { requested: 'Solicitud', approved: 'Aprobación', dispatched: 'Despacho', received: 'Recepción', cancelled: 'Cancelación' }
  return <section aria-label="Detalle de mercadería"><h2>Transferencia: {stateLabel[t.status]}</h2><p>{t.sourceBranchName} → {t.destinationBranchName}</p><p>Solicitada UTC: <time dateTime={t.requestedAt}>{t.requestedAt}</time> · Último cambio UTC: {t.updatedAt}</p>
    <div className="operation-table"><table><caption>Productos y presentaciones solicitados · snapshots persistidos</caption><thead><tr><th>Línea / producto</th><th>Presentación</th><th>Cantidad</th><th>Conversión</th><th>Base</th></tr></thead><tbody>{t.lines.map((line, index) => <tr key={line.id}><td>{index + 1} · <ProductLabel id={line.businessProductId} /></td><td>{line.unitNameSnapshot}</td><td>{decimalLabel(line.requestedQuantity)}</td><td>{decimalLabel(line.conversionToBaseSnapshot)}</td><td>{decimalLabel(line.requestedBaseQuantity)}</td></tr>)}</tbody></table></div>
    {!!t.allocations.length && <div className="operation-table"><table><caption>Lotes y recepción persistidos</caption><thead><tr><th>Línea / lote</th><th>Vencimiento</th><th>Despachado base</th><th>Recibido base</th><th>Diferencia base</th></tr></thead><tbody>{t.allocations.map(row => <tr key={row.id}><td>Línea {t.lines.findIndex(line => equalId(line.id, row.transferLineId)) + 1} · {row.batchNumber ?? 'Sin número'}</td><td>{row.expirationDate ?? 'No informado'}</td><td>{decimalLabel(row.dispatchedQuantityBase)}</td><td>{row.receivedQuantityBase === null ? 'Pendiente' : decimalLabel(row.receivedQuantityBase)}</td><td>{row.differenceBase === null ? 'Pendiente' : decimalLabel(row.differenceBase)}</td></tr>)}</tbody></table></div>}
    <h3>Historial</h3><ol>{t.events.map(event => <li key={event.id}>{labels[event.eventType]} · <time dateTime={event.occurredAt}>{event.occurredAt}</time> UTC · {equalId(event.actorId, w.session.userId) ? 'Tu usuario' : 'Otro operador registrado'}{event.reason && <> · Motivo: {event.reason}</>}</li>)}</ol>
    {(source.isFetching || destination.isFetching) && <p>Verificando acceso operacional en origen y destino…</p>}
    {source.isError && <ApiErrorState error={source.error} title="No pudimos verificar acceso al origen" retry={() => void source.refetch()} />}
    {destination.isError && <ApiErrorState error={destination.error} title="No pudimos verificar acceso al destino" retry={() => void destination.refetch()} />}
    <p>El servidor autoriza cada transición: aprobar/despachar exige Owner o Pharmacist en origen; recibir exige Owner o Pharmacist en destino. Cancelar depende del estado, origen o solicitante. El acceso a la sucursal actual no concede acceso a la otra.</p>
    {t.status === 'requested' && <fieldset disabled={locked || !permissions.approve}><legend>Aprobar solicitud desde el origen</legend><label><input type="checkbox" checked={approve} onChange={event => setApprove(event.target.checked)} /> Revisé la solicitud y autorizo su aprobación.</label><Button disabled={!approve} onClick={() => transition('approve')}>Confirmar aprobación</Button>{!permissions.approve && <p>Aprobación no disponible sin acceso operacional vigente y rol autorizado en origen.</p>}</fieldset>}
    {(t.status === 'requested' || t.status === 'approved') && <fieldset disabled={locked || !permissions.cancel}><legend>Cancelar antes del despacho</legend><TextField label="Motivo de cancelación" value={reason} maxLength={512} onChange={event => { setReason(event.target.value); setCancel(false) }} /><label><input type="checkbox" checked={cancel} onChange={event => setCancel(event.target.checked)} /> Confirmo cancelar la transferencia por el motivo indicado.</label><Button variant="secondary" disabled={!cancel || !reason.trim() || reason.trim().length > 512} onClick={() => transition('cancel', reason.trim())}>Confirmar cancelación</Button>{!permissions.cancel && <p>Cancelación no disponible para el acceso y solicitante actuales.</p>}</fieldset>}
    {t.status === 'approved' && (permissions.dispatch ? <DispatchGoods transfer={t} disabled={locked} dispatch={rows => transition('dispatch', rows)} /> : <p>Despacho no disponible: requiere acceso operacional y rol autorizado en origen.</p>)}
    {t.status === 'in_transit' && (permissions.receive ? <ReceiveGoods transfer={t} disabled={locked} receive={rows => transition('receive', rows)} /> : <p>Recepción no disponible: requiere acceso operacional y rol autorizado en destino.</p>)}
    {(t.status === 'received' || t.status === 'cancelled') && <p>Estado terminal consultado; no se ofrecen nuevas transiciones.</p>}
  </section>
}
