import { useId, useState } from 'react'
import type { Branch } from '../access'
import { ProductLookup } from '../catalog'
import type { ProductDetail } from '../catalog'
import { decimalLabel } from '../../shared/api/decimal'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { equalId } from './api'
import type { TransferLineInput } from './api'
import { requestLine, validRequest } from './model'
import type { RequestLine } from './model'

export function RequestGoods({ branches, destinationId, destinationName, disabled, create }: { branches: readonly Branch[]; destinationId: string; destinationName: string; disabled: boolean; create: (source: string, lines: TransferLineInput[]) => void }) {
  const [source, setSource] = useState(''); const [product, setProduct] = useState<ProductDetail | null>(null); const [unitId, setUnitId] = useState(''); const [quantity, setQuantity] = useState('')
  const [lines, setLines] = useState<RequestLine[]>([]); const [reviewed, setReviewed] = useState(false); const id = useId()
  const candidate = product ? requestLine(product, product.units.find(row => equalId(row.id, unitId)), quantity) : null
  const sources = branches.filter(branch => !equalId(branch.id, destinationId))
  return <section aria-label="Solicitar mercadería"><h2>Solicitar mercadería</h2><p>Destino operativo: {destinationName}. Solicitar no reserva ni descuenta stock. Orígenes limitados al directorio consultable; no se presupone autorización para aprobar o despachar desde ellos.</p>
    <fieldset disabled={disabled}><legend>Origen, productos y cantidades</legend>
      <label htmlFor={id}>Sucursal origen</label><select id={id} value={source} onChange={event => { setSource(event.target.value); setReviewed(false) }}><option value="">Seleccionar origen consultable</option>{sources.map(branch => <option key={branch.id} value={branch.id}>{branch.name}</option>)}</select>
      {!sources.length && <p>No hay otro origen consultable. El contrato no ofrece un directorio adicional para seleccionar sucursales no asignadas; no se admiten UUID libres como alternativa.</p>}
      <ProductLookup disabled={disabled} onSelect={value => { setProduct(value); setUnitId(''); setQuantity('') }} />
      {product && <><p>{product.product.name}</p>{!product.units.some(unit => unit.isActive) && <p>Sin presentaciones activas. Owner debe configurar o activar una presentación desde Productos antes de solicitar este producto.</p>}
        <label htmlFor={id + '-unit'}>Presentación solicitada</label><select id={id + '-unit'} value={unitId} onChange={event => setUnitId(event.target.value)}><option value="">Seleccionar presentación</option>{product.units.filter(unit => unit.isActive).map(unit => <option key={unit.id} value={unit.id}>{unit.name} · {decimalLabel(unit.conversionToBase)} unidades base</option>)}</select>
        <TextField label="Cantidad solicitada" inputMode="decimal" value={quantity} maxLength={60} onChange={event => setQuantity(event.target.value)} />
        {candidate && <p>Conversión base exacta: {decimalLabel(candidate.base)}</p>}
        <Button disabled={!candidate || !validRequest([...lines, candidate])} onClick={() => { if (candidate && validRequest([...lines, candidate])) { setLines(current => [...current, candidate]); setQuantity(''); setReviewed(false) } }}>Agregar producto a la solicitud</Button>
      </>}
      <p>Máximo 200 líneas distintas por producto/presentación; cantidades positivas numeric(28,12), sin redondeo.</p>
      <ul>{lines.map((row, index) => <li key={row.input.businessProductId + '/' + row.input.productUnitId}>{row.name} · {decimalLabel(row.input.quantity)} {row.unit} = {decimalLabel(row.base)} base <Button variant="quiet" onClick={() => { setLines(current => current.filter((_, position) => position !== index)); setReviewed(false) }}>Quitar {row.name} · {row.unit}</Button></li>)}</ul>
      <label><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Revisé origen, destino, productos y cantidades; autorizo enviar esta solicitud una vez.</label>
      <Button disabled={!reviewed || !sources.some(branch => equalId(branch.id, source)) || !validRequest(lines) || Boolean(quantity.trim())} onClick={() => create(source, lines.map(row => row.input))}>Enviar solicitud de mercadería</Button>
      {quantity.trim() && <p>Agrega la cantidad en edición o vacía el campo antes de enviar.</p>}
    </fieldset>
  </section>
}
