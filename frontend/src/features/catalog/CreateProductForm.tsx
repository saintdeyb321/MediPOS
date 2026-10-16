import { useState } from 'react'
import { TextField } from '../../shared/ui/TextField'
import { Button } from '../../shared/ui/Button'
import { useRuntime, useWorkspace } from '../access'
import { catalogApi } from './api'
import type { Category, GlobalProduct } from './api'
import { emptyMedicine, medicineInput, prices, required } from './forms'
import { MedicineFieldsForm, MedicineView, PriceInputs } from './ProductFields'
import type { CatalogWrite } from './useCatalogWrite'

export function CreateProductForm({ categories, global, write }: { categories: Category[]; global: GlobalProduct | null; write: CatalogWrite }) {
  const w = useWorkspace(); const { http } = useRuntime()
  const [code, setCode] = useState(''); const [name, setName] = useState(''); const [brand, setBrand] = useState(''); const [barcode, setBarcode] = useState('')
  const [categoryId, setCategory] = useState(''); const [type, setType] = useState<'' | 'medicine' | 'retail'>('')
  const [price, setPrice] = useState({ retail: '', wholesale: '' }); const [active, setActive] = useState(true)
  const [medicine, setMedicine] = useState(emptyMedicine); const [reviewed, setReviewed] = useState(false)
  const priceInput = prices(price); const clinical = medicineInput(medicine)
  const valid = required(code, 64) && priceInput && (global || type && required(name, 256) && required(brand, 200) && barcode.trim().length <= 128 &&
    categories.some(row => row.id === categoryId && row.isActive) && (type === 'retail' || clinical))
  return <form aria-label={global ? 'Asociar producto global' : 'Crear producto local'} onSubmit={event => {
    event.preventDefault(); if (!http || !valid || !reviewed || !priceInput) return
    const api = catalogApi(http)
    if (global) void write.run('global', undefined, async signal => (await api.fromGlobal(w, { globalProductId: global.id, internalCode: code.trim(), ...priceInput, isActive: active }, signal)).id, code.trim())
    else if (type) void write.run('create', undefined, async signal => (await api.create(w, { internalCode: code.trim(), name: name.trim(), productType: type, categoryId,
      brandOrLaboratory: brand.trim(), barcode: barcode.trim() || null, medicine: type === 'medicine' ? clinical : null, ...priceInput, isActive: active }, signal)).id, code.trim())
  }}>
    <fieldset disabled={write.locked} onChange={() => setReviewed(false)}><legend>{global ? `Asociar: ${global.name}` : 'Nuevo producto exclusivo del negocio'}</legend>
      <TextField label="Código interno" value={code} maxLength={64} required onChange={event => setCode(event.target.value)} />
      {!global && <>
        <label>Tipo de producto <select aria-label="Tipo de producto" required value={type} onChange={event => setType(event.target.value === 'medicine' ? 'medicine' : event.target.value === 'retail' ? 'retail' : '')}><option value="">Selecciona</option><option value="medicine">Medicamento</option><option value="retail">Venta general</option></select></label>
        <TextField label="Nombre del producto" value={name} maxLength={256} required onChange={event => setName(event.target.value)} />
        <label>Categoría <select aria-label="Categoría" value={categoryId} required onChange={event => setCategory(event.target.value)}><option value="">Selecciona una categoría existente</option>{categories.filter(row => row.isActive).map(row => <option key={row.id} value={row.id}>{row.name}</option>)}</select></label>
        {!categories.length && <p>No hay categorías disponibles. Su creación requiere administración independiente; esta pantalla no concede ese permiso.</p>}
        <TextField label="Marca o laboratorio documentado" value={brand} maxLength={200} required onChange={event => setBrand(event.target.value)} hint="Puede ser una marca verificable. No atribuyas el titular del registro como fabricante sin respaldo." />
        <TextField label="Código de barras (opcional)" value={barcode} maxLength={128} onChange={event => setBarcode(event.target.value)} />
        {type === 'medicine' && <MedicineFieldsForm value={medicine} change={setMedicine} disabled={write.locked} />}
      </>}
      {global && <><MedicineView value={global.medicine} /><p>Se copiarán los atributos del catálogo publicado al negocio. Vincular no crea stock físico.</p></>}
      <PriceInputs value={price} change={setPrice} disabled={write.locked} />
      <label><input type="checkbox" checked={active} onChange={event => setActive(event.target.checked)} /> Producto activo</label>
    </fieldset>
    <label><input type="checkbox" checked={reviewed} disabled={write.locked} onChange={event => setReviewed(event.target.checked)} /> Revisé la identidad, los atributos documentados y los precios de este producto.</label>
    <Button type="submit" pending={write.busy} disabled={write.locked || !valid || !reviewed}>{global ? 'Asociar al negocio' : 'Crear producto local'}</Button>
    <p>Esta operación no publica globalmente ni crea inventario. No reenvíes una solicitud con resultado incierto.</p>
  </form>
}
