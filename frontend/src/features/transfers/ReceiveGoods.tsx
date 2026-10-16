import { useState } from 'react'
import { decimalInput, decimalLabel, maximumQuantity } from '../../shared/api/decimal'
import { TextField } from '../../shared/ui/TextField'
import { Button } from '../../shared/ui/Button'
import type { ReceiptSelection, Transfer } from './api'
import { difference, total, validReceipt } from './model'
import { equalId } from './api'

export function ReceiveGoods({ transfer, disabled, receive }: { transfer: Transfer; disabled: boolean; receive: (rows: ReceiptSelection[]) => void }) {
  const [inputs, setInputs] = useState<Record<string, string>>({}); const [reviewed, setReviewed] = useState(false)
  const rows = transfer.allocations.flatMap(row => { const quantityBase = decimalInput(inputs[row.id] ?? '', maximumQuantity, 12, true); return quantityBase ? [{ allocationId: row.id, quantityBase }] : [] })
  const valid = validReceipt(transfer, rows)
  return <section aria-label="Recibir mercadería"><h3>Recepción en {transfer.destinationBranchName}</h3><p>Indica lo recibido físicamente en cada asignación, incluido cero. El faltante queda registrado; no se compensa ni se repone automáticamente.</p>
    <fieldset disabled={disabled}><legend>Cantidades recibidas por lote despachado</legend>
      {transfer.allocations.map((allocation, index) => <div key={allocation.id}><p>Asignación {index + 1} · Lote {allocation.batchNumber ?? 'Sin número'} · Vencimiento {allocation.expirationDate ?? 'No informado'} · Despachado {decimalLabel(allocation.dispatchedQuantityBase)} base</p>
        <TextField label={`Recibido base de asignación ${index + 1}`} inputMode="decimal" maxLength={60} value={inputs[allocation.id] ?? ''} onChange={event => { setInputs(current => ({ ...current, [allocation.id]: event.target.value })); setReviewed(false) }} />
      </div>)}
      {valid ? transfer.lines.map((line, index) => {
        const allocations = transfer.allocations.filter(row => equalId(row.transferLineId, line.id))
        const sent = total(allocations.map(row => row.dispatchedQuantityBase)); const received = total(rows.filter(row => allocations.some(allocation => equalId(allocation.id, row.allocationId))).map(row => row.quantityBase))
        return <p key={line.id}>Línea {index + 1}: despachado {decimalLabel(sent)} · Recibido {decimalLabel(received)} · Diferencia {decimalLabel(difference(sent, received))} base.</p>
      }) : <p>Completa todas las asignaciones: cantidades entre cero y lo despachado, hasta doce decimales sin redondeo.</p>}
      <label><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Revisé los lotes, las cantidades físicamente recibidas y las diferencias; autorizo la recepción.</label>
      <Button disabled={!valid || !reviewed} onClick={() => receive(rows)}>Confirmar recepción de mercadería</Button>
    </fieldset>
  </section>
}
