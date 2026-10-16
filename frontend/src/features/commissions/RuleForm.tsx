import { useState } from 'react'
import { ProductLookup } from '../catalog'
import type { ProductDetail } from '../catalog'
import { decimalInput, maximumMoney } from '../../shared/api/decimal'
import { TextField } from '../../shared/ui/TextField'
import { Button } from '../../shared/ui/Button'
import { validRule } from './api'
import type { RuleInput } from './api'

export function RuleForm({ locked, save }: { locked: boolean; save: (productId: string, input: RuleInput) => void }) {
  const [product, setProduct] = useState<ProductDetail | null>(null); const [type, setType] = useState<RuleInput['ruleType']>('fixed'); const [text, setText] = useState(''); const [from, setFrom] = useState(''); const [until, setUntil] = useState(''); const [review, setReview] = useState('')
  const value = decimalInput(text, maximumMoney, 4); const input = value ? { ruleType: type, value, validFrom: from.trim(), validUntil: until.trim() || null } : null
  const signature = JSON.stringify([product?.product.id, type, text, from, until])
  return <section aria-label="Nueva regla de comisión"><h2>Crear/reemplazar regla de un producto</h2><p>El servidor crea una revisión y desactiva la regla anterior del producto atómicamente. Se conserva el historial y no se recalculan comisiones ya registradas.</p><fieldset disabled={locked}><legend>Producto activo y regla</legend>
    <ProductLookup disabled={locked} onSelect={setProduct} />{product && <p>Producto consultado: {product.product.name}</p>}
    <label htmlFor="commission-type">Tipo de regla</label><select id="commission-type" value={type} onChange={event => setType(event.target.value as RuleInput['ruleType'])}><option value="fixed">Fijo por unidad base vendida (PEN)</option><option value="percentage">Porcentaje del importe neto de la línea</option></select>
    <TextField label={type === 'fixed' ? 'Comisión fija PEN por unidad base' : 'Porcentaje de comisión'} inputMode="decimal" value={text} maxLength={60} onChange={event => setText(event.target.value)} /><p>Positivo, máximo cuatro decimales. Porcentaje hasta 100; fijo hasta {maximumMoney.literal} PEN.</p>
    <TextField label="Vigente desde UTC" placeholder="AAAA-MM-DDTHH:mm:ssZ" value={from} maxLength={40} onChange={event => setFrom(event.target.value)} /><TextField label="Vigente hasta UTC (opcional, exclusivo)" placeholder="AAAA-MM-DDTHH:mm:ssZ" value={until} maxLength={40} onChange={event => setUntil(event.target.value)} /><p>Introduce instantes UTC explícitos. El final, si se indica, debe ser posterior al inicio. No se convierte silenciosamente una hora local.</p>
    <label><input type="checkbox" checked={review === signature} onChange={event => setReview(event.target.checked ? signature : '')} /> Revisé producto, valor y vigencia; autorizo crear esta regla y reemplazar la anterior.</label>
    <Button disabled={!product?.product.isActive || !input || !validRule(input) || review !== signature} onClick={() => { if (product && input && validRule(input)) { setReview(''); save(product.product.id, input) } }}>Confirmar nueva regla de comisión</Button>
  </fieldset></section>
}
