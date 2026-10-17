import { useId, useState } from 'react'
import { ProductLookup } from '../catalog'
import type { ProductDetail } from '../catalog'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { decimalLabel } from '../../shared/api/decimal'
import { prepareLine, receiptTotal } from './receipt'
import type { ReceiptLine } from './receipt'
import type { PurchaseLineInput } from './api'

export function ReceiptEditor({ disabled, replacing, save }: { disabled: boolean; replacing: boolean; save: (lines: PurchaseLineInput[]) => void }) {
  const [detail, setDetail] = useState<ProductDetail | null>(null); const [unitId, setUnitId] = useState(''); const selectId = useId()
  const [quantity, setQuantity] = useState(''); const [cost, setCost] = useState(''); const [batch, setBatch] = useState(''); const [expiration, setExpiration] = useState('')
  const [lines, setLines] = useState<ReceiptLine[]>([]); const [review, setReview] = useState(false)
  const units = detail?.units.filter(row => row.isActive) ?? []; const unit = units.find(row => row.id === unitId)
  const candidate = detail ? prepareLine(detail, unit, quantity, cost, batch, expiration) : null
  return <section aria-label="Productos y lotes"><h2>Productos y lotes</h2>
    <p>Estas líneas son locales hasta guardarlas. {replacing ? 'Guardar reemplazará todas las líneas del borrador; revisa el conjunto completo. No edita una compra confirmada.' : 'Guardar prepara la recepción sin ingresar stock.'}</p>
    <fieldset disabled={disabled}><legend>Agregar línea</legend>
      <ProductLookup disabled={disabled} onSelect={value => { setDetail(value); setUnitId(''); setQuantity(''); setCost(''); setBatch(''); setExpiration('') }} />
      {detail && <><p>Producto: {detail.product.name} · {detail.product.productType === 'medicine' ? 'Medicamento: lote y vencimiento obligatorios.' : 'Venta general.'}</p>
        {!units.length && <p role="alert">Sin presentaciones activas. Owner debe configurar o activar una presentación desde Productos antes de recibir este producto.</p>}
        <label htmlFor={selectId}>Presentación de compra</label><select id={selectId} value={unitId} onChange={event => setUnitId(event.target.value)}><option value="">Elige una presentación</option>{units.map(row => <option key={row.id} value={row.id}>{row.name} · {decimalLabel(row.conversionToBase)} unidades base</option>)}</select>
        <TextField label="Cantidad de presentaciones" inputMode="decimal" value={quantity} maxLength={90} onChange={event => setQuantity(event.target.value)} />
        <TextField label="Costo por presentación (S/)" hint="Costo de la presentación elegida, no precio de venta ni costo por unidad base si eliges caja." inputMode="decimal" value={cost} maxLength={90} onChange={event => setCost(event.target.value)} />
        <TextField label="Número de lote" value={batch} maxLength={128} onChange={event => setBatch(event.target.value)} />
        <TextField label="Vencimiento del lote" type="date" value={expiration} onChange={event => setExpiration(event.target.value)} />
        {candidate ? <p>Cantidad base exacta: {decimalLabel(candidate.baseQuantity)}</p> : <p>Completa presentación, cantidad positiva, costo no negativo exacto y datos del lote. Sin conversiones ni redondeos silenciosos.</p>}
        <Button disabled={!candidate} onClick={() => { if (candidate) { setLines(current => [...current, candidate]); setReview(false); setQuantity(''); setCost(''); setBatch(''); setExpiration('') } }}>Agregar línea a la recepción</Button>
      </>}
    </fieldset>
    {lines.length > 0 && <><div className="operation-table"><table><caption>Líneas locales · aún sin stock</caption><thead><tr><th>Producto / presentación</th><th>Cantidad / base</th><th>Costo por presentación</th><th>Lote / vencimiento</th><th>Acción</th></tr></thead><tbody>{lines.map(row => <tr key={row.key}><td>{row.name} · {row.unitName}</td><td>{decimalLabel(row.input.quantity)} / {decimalLabel(row.baseQuantity)}</td><td>S/ {decimalLabel(row.input.unitCost, 2)}</td><td>{row.input.batchNumber ?? 'No informado'} / {row.input.expirationDate ?? 'No informado'}</td><td><Button variant="quiet" disabled={disabled} onClick={() => { setLines(current => current.filter(item => item.key !== row.key)); setReview(false) }}>Quitar {row.name}</Button></td></tr>)}</tbody></table></div>
      <p>Importe documental calculado (cantidad × costo, sin redondeo adicional): S/ {decimalLabel(receiptTotal(lines.map(row => row.input)), 2)}</p>
      <label><input type="checkbox" checked={review} disabled={disabled} onChange={event => setReview(event.target.checked)} /> Revisé todas las líneas, costos por presentación, lotes y vencimientos{replacing ? ', y autorizo reemplazar el borrador completo' : ''}.</label>
      <Button disabled={disabled || !review} onClick={() => save(lines.map(row => row.input))}>Guardar líneas y revisar recepción</Button>
    </>}
  </section>
}
