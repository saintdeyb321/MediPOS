import { useEffect, useState } from 'react'
import { useRuntime, useWorkspace } from '../access'
import { catalogApi } from './api'
import type { Category, ProductDetail } from './api'
import type { CatalogWrite } from './useCatalogWrite'
import { medicineInput, required } from './forms'
import type { MedicineFields } from './forms'
import { MedicineFieldsForm } from './ProductFields'
import { TextField } from '../../shared/ui/TextField'
import { Button } from '../../shared/ui/Button'

export function EditLocalProduct({ detail, categories, write, onDirty }: { detail: ProductDetail; categories: Category[]; write: CatalogWrite; onDirty?: (dirty: boolean) => void }) {
  const w = useWorkspace(); const { http } = useRuntime(); const p = detail.product
  const [name, setName] = useState(p.name); const [brand, setBrand] = useState(p.brandOrLaboratory); const [barcode, setBarcode] = useState(p.barcode ?? ''); const [categoryId, setCategory] = useState(p.categoryId)
  const [medicine, setMedicine] = useState<MedicineFields>({ components: p.medicine?.components.map(c => ({ ...c })) ?? [{ ingredient: '', strengthNormalized: '' }], dosageForm: p.medicine?.dosageForm ?? '', route: p.medicine?.route ?? '', sanitaryRegistration: p.medicine?.sanitaryRegistration ?? '' })
  const [reviewed, setReviewed] = useState(false)
  const [baseline] = useState(() => JSON.stringify({ name, brand, barcode, categoryId, medicine }))
  useEffect(() => { onDirty?.(JSON.stringify({ name, brand, barcode, categoryId, medicine }) !== baseline) }, [name, brand, barcode, categoryId, medicine, baseline, onDirty])
  const clinical = p.productType === 'medicine' ? medicineInput(medicine) : null
  const valid = !p.globalProductId && Boolean(detail.revision) && required(name, 256) && required(brand, 200) && barcode.trim().length <= 128 && categories.some(c => c.id === categoryId && c.isActive) && (p.productType !== 'medicine' || Boolean(clinical))
  return <form aria-label="Corregir producto local" onSubmit={event => { event.preventDefault(); if (http && valid && reviewed) { setReviewed(false); void write.run('metadata', p.id, async signal => (await catalogApi(http).correctLocal(w, p.id, { expectedRevision: detail.revision, name: name.trim(), categoryId, brandOrLaboratory: brand.trim(), barcode: barcode.trim() || null, medicine: clinical }, signal)).id) } }}>
    <p>Corrección documentada del producto local. No cambia precios, tipo, código interno, referencias globales ni snapshots comerciales históricos. No convierte dosis ni declara equivalencia terapéutica.</p>
    <fieldset disabled={write.locked || Boolean(p.globalProductId)} onChange={() => setReviewed(false)}><legend>Datos vigentes</legend>
      <TextField label="Nombre del producto" value={name} maxLength={256} onChange={e => setName(e.target.value)} />
      <TextField label="Marca o laboratorio documentado" value={brand} maxLength={200} onChange={e => setBrand(e.target.value)} />
      <TextField label="Código de barras" value={barcode} maxLength={128} onChange={e => setBarcode(e.target.value)} />
      <label>Categoría<select aria-label="Categoría del producto local" value={categoryId} onChange={e => setCategory(e.target.value)}>{categories.filter(c => c.isActive).map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label>
      {p.productType === 'medicine' && <MedicineFieldsForm value={medicine} change={setMedicine} disabled={write.locked} />}
    </fieldset>
    <label><input type="checkbox" checked={reviewed} disabled={write.locked || !valid} onChange={e => setReviewed(e.target.checked)} /> Verifiqué los atributos contra su fuente y autorizo la corrección auditada.</label>
    <Button type="submit" disabled={write.locked || !valid || !reviewed}>Guardar corrección documentada</Button>
  </form>
}
