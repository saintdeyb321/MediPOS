import { useState } from 'react'
import { ProductLabel } from '../catalog'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { FormFooter } from '../../shared/ui/FormLayout'
import { add, compare, decimalInput, decimalLabel, isSystemDecimal, maximumQuantity, systemDecimalInput, zero } from '../../shared/api/decimal'
import type { LotStock } from './api'
import type { InventoryWrite } from './useInventoryWrite'

export function AdjustmentForm({ lot, write, onSaved }: { lot: LotStock; write: InventoryWrite; onSaved: () => void }) {
  const [quantity, setQuantity] = useState(''); const [reason, setReason] = useState(''); const [reviewed, setReviewed] = useState(false)
  const delta = systemDecimalInput(quantity, false, true)
  const after = delta ? add(lot.quantityAvailableBase, delta) : null
  const valid = after && isSystemDecimal(after) && compare(after, zero) >= 0 && Boolean(reason.trim()) && reason.trim().length <= 512
  return <form aria-label="Ajustar lote" onSubmit={event => { event.preventDefault(); if (valid && delta && reviewed) void write.adjust(lot.inventoryLotId, delta, reason.trim()).then(saved => { if (saved) { setQuantity(''); setReason(''); setReviewed(false); onSaved() } }) }}>
    <h2>Ajuste auditado de lote</h2><p><ProductLabel id={lot.businessProductId} /> · Lote {lot.batchNumber ?? 'Sin número'} · Vencimiento {lot.expirationDate ?? 'No informado'}</p>
    <p>Saldo consultado: {decimalLabel(lot.quantityAvailableBase)} unidades base. El servidor vuelve a bloquear y validar el saldo al aplicar el movimiento.</p>
    <fieldset disabled={write.locked}><legend>Movimiento con motivo obligatorio</legend>
      <TextField label="Variación en unidades base (+ entrada, - salida)" inputMode="text" value={quantity} maxLength={90} onChange={event => { setQuantity(event.target.value); setReviewed(false) }} />
      <TextField label="Motivo del ajuste" value={reason} maxLength={512} required onChange={event => { setReason(event.target.value); setReviewed(false) }} />
      {valid && after ? <p>Saldo estimado después del ajuste: {decimalLabel(after)}. Se verificará con la respuesta real.</p> : <p>Indica una variación distinta de cero, exactamente representable y un motivo; no puede dejar saldo negativo.</p>}
      <label><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Revisé el lote, la variación y el motivo; autorizo registrar el movimiento.</label>
      <FormFooter><Button type="submit" disabled={!valid || !reviewed} pending={write.busy}>Registrar ajuste una vez</Button></FormFooter>
    </fieldset>
  </form>
}
export function MinimumForm({ productId, write, onSaved }: { productId: string; write: InventoryWrite; onSaved?: () => void }) {
  const [text, setText] = useState(''); const [reviewed, setReviewed] = useState(false)
  const minimum = decimalInput(text, maximumQuantity, 12, true)
  return <form aria-label="Configurar stock mínimo" onSubmit={event => { event.preventDefault(); if (minimum && reviewed) void write.minimum(productId, minimum).then(saved => { if (saved) { setText(''); setReviewed(false); onSaved?.() } }) }}><h2>Stock mínimo · propietario</h2>
    <p><ProductLabel id={productId} />. La API no ofrece lectura del mínimo actual en este módulo; el campo vacío no significa cero. Guardar establece el valor introducido para esta sucursal.</p>
    <fieldset disabled={write.locked}><legend>Nuevo mínimo en unidades base</legend>
      <TextField label="Mínimo de stock base" inputMode="decimal" value={text} maxLength={60} onChange={event => { setText(event.target.value); setReviewed(false) }} />
      <label><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Autorizo establecer este mínimo en la sucursal actual.</label>
      <FormFooter><Button type="submit" disabled={!minimum || !reviewed} pending={write.busy}>Guardar mínimo</Button></FormFooter>
    </fieldset>
  </form>
}
