import { useId, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { inventoryApi } from '../inventory'
import { ProductLabel } from '../catalog'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { compare, decimalInput, decimalLabel, maximumQuantity, zero } from '../../shared/api/decimal'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { PageControls } from '../../shared/ui/PageControls'
import type { DispatchSelection, Transfer } from './api'
import { equalId } from './api'
import { total, validDispatch } from './model'
import type { LotSelection } from './model'

export function DispatchGoods({ transfer, disabled, dispatch }: { transfer: Transfer; disabled: boolean; dispatch: (selections: DispatchSelection[]) => void }) {
  const w = useWorkspace(); const { http } = useRuntime(); const [lineId, setLineId] = useState(transfer.lines[0]?.id ?? '')
  const [offset, setOffset] = useState(0); const [lotId, setLotId] = useState(''); const [text, setText] = useState(''); const [rows, setRows] = useState<LotSelection[]>([]); const [reviewed, setReviewed] = useState(false)
  const control = useId(); const line = transfer.lines.find(row => equalId(row.id, lineId)); const scope = { tenantId: w.tenantId, branchId: transfer.sourceBranchId }
  const lots = useQuery({ queryKey: scopedKey(scope, 'inventory-lots', w.session.userId, w.access.membershipId, 'transfer', transfer.id, line?.businessProductId, offset),
    queryFn: ({ signal }) => { if (!http || !line) throw new ApiError(0, 'client.https_required'); return inventoryApi(http).lots(scope, line.businessProductId, offset, 20, signal) },
    enabled: Boolean(http && line), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const lot = !lots.isError ? lots.data?.find(row => equalId(row.inventoryLotId, lotId) && Boolean(line && equalId(row.businessProductId, line.businessProductId))) : undefined
  const quantity = decimalInput(text, maximumQuantity, 12)
  const already = lot ? total(rows.filter(row => equalId(row.lot.inventoryLotId, lot.inventoryLotId)).map(row => row.input.quantityBase)) : zero
  const assigned = total(rows.filter(row => equalId(row.input.transferLineId, lineId)).map(row => row.input.quantityBase))
  const canAdd = Boolean(line && lot && quantity && compare(total([already, quantity]), lot.quantityAvailableBase) <= 0 && compare(total([assigned, quantity]), line.requestedBaseQuantity) <= 0 && rows.length < 1000 && !rows.some(row => equalId(row.input.transferLineId, line.id) && equalId(row.input.inventoryLotId, lot.inventoryLotId)))
  const valid = validDispatch(transfer, rows)
  return <section aria-label="Despachar por lotes"><h3>Despacho desde {transfer.sourceBranchName}</h3><p>Asigna manualmente lotes y cantidades base. No hay selección automática FEFO. Los saldos son una consulta; el servidor bloquea y valida los lotes al despachar.</p>
    <fieldset disabled={disabled}><legend>Asignaciones del despacho</legend>
      <label htmlFor={control}>Línea aprobada</label><select id={control} value={lineId} onChange={event => { setLineId(event.target.value); setOffset(0); setLotId(''); setText('') }}>{transfer.lines.map((row, index) => <option key={row.id} value={row.id}>Línea {index + 1} · {row.unitNameSnapshot} · {decimalLabel(row.requestedBaseQuantity)} base</option>)}</select>
      {line && <p>Producto: <ProductLabel id={line.businessProductId} /> · Solicitado: {decimalLabel(line.requestedBaseQuantity)} base. Ya asignado: {decimalLabel(total(rows.filter(row => equalId(row.input.transferLineId, line.id)).map(row => row.input.quantityBase)))}</p>}
      {lots.isFetching && <p>Consultando lotes del origen…</p>}{lots.isError && <ApiErrorState error={lots.error} title="No pudimos consultar los lotes del origen" />}
      {lots.data && !lots.isError && <div className="operation-table"><table><caption>Lotes del producto en la sucursal origen</caption><thead><tr><th>Lote</th><th>Vencimiento</th><th>Disponible base</th><th>Selección</th></tr></thead><tbody>{lots.data.filter(row => line && equalId(row.businessProductId, line.businessProductId)).map(row => <tr key={row.inventoryLotId}><td>{row.batchNumber ?? 'Sin número'}</td><td>{row.expirationDate ?? 'No informado'}</td><td>{decimalLabel(row.quantityAvailableBase)}</td><td><Button variant="quiet" disabled={compare(row.quantityAvailableBase, zero) <= 0} onClick={() => { setLotId(row.inventoryLotId); setText('') }}>Elegir lote {row.batchNumber ?? 'sin número'}</Button></td></tr>)}</tbody></table>{!lots.data.length && <p>No hay lotes en esta página. No es evidencia de stock en otra página.</p>}</div>}
      <PageControls offset={offset} count={lots.isError ? undefined : lots.data?.length} busy={lots.isFetching} change={value => { setOffset(value); setLotId('') }} refresh={() => { setReviewed(false); setLotId(''); void lots.refetch() }} />
      {lot && <p>Lote elegido: {lot.batchNumber ?? 'Sin número'} · Disponible {decimalLabel(lot.quantityAvailableBase)} base</p>}
      <TextField label="Cantidad base del lote" inputMode="decimal" value={text} maxLength={60} onChange={event => setText(event.target.value)} />
      <Button disabled={!canAdd || lots.isFetching} onClick={() => { if (canAdd && line && lot && quantity) { setRows(current => [...current, { lot, input: { transferLineId: line.id, inventoryLotId: lot.inventoryLotId, quantityBase: quantity } }]); setText(''); setLotId(''); setReviewed(false) } }}>Agregar asignación de lote</Button>
      <ul>{rows.map((row, index) => <li key={row.input.transferLineId + '/' + row.input.inventoryLotId}>Línea {transfer.lines.findIndex(line => equalId(line.id, row.input.transferLineId)) + 1} · Lote {row.lot.batchNumber ?? 'Sin número'} · {row.lot.expirationDate ?? 'Sin vencimiento'} · {decimalLabel(row.input.quantityBase)} base <Button variant="quiet" onClick={() => { setRows(current => current.filter((_, position) => position !== index)); setReviewed(false) }}>Quitar asignación {index + 1}</Button></li>)}</ul>
      {!valid && <p>Falta cubrir exactamente todas las líneas, o una asignación supera la disponibilidad agregada del lote. Máximo 1000 asignaciones distintas línea/lote.</p>}
      <label><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Revisé el despacho completo, lotes, vencimientos y cantidades exactas.</label>
      <Button disabled={!valid || !reviewed || Boolean(text.trim())} onClick={() => dispatch(rows.map(row => row.input))}>Confirmar despacho de mercadería</Button>
    </fieldset>
  </section>
}
