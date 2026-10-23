import { useState } from 'react'
import { TextField } from '../../shared/ui/TextField'
import { Button } from '../../shared/ui/Button'
import { Steps } from '../../shared/ui/Steps'
import { FormFooter } from '../../shared/ui/FormLayout'
import { decimalLabel } from '../../shared/api/decimal'
import { useRuntime, useWorkspace } from '../access'
import { catalogApi } from './api'
import type { Category, GlobalProduct } from './api'
import { emptyMedicine, medicineInput, prices, required } from './forms'
import { MedicineFieldsForm, MedicineView, PriceInputs } from './ProductFields'
import type { CatalogWrite } from './useCatalogWrite'

export function CreateProductForm({ categories, global, write, onDirty }: { categories: Category[]; global: GlobalProduct | null; write: CatalogWrite; onDirty?: () => void }) {
  const w = useWorkspace(); const { http } = useRuntime()
  const [code, setCode] = useState(''); const [name, setName] = useState(''); const [brand, setBrand] = useState(''); const [barcode, setBarcode] = useState('')
  const [categoryId, setCategory] = useState(''); const [type, setType] = useState<'' | 'medicine' | 'retail'>('')
  const [price, setPrice] = useState({ retail: '', wholesale: '' }); const [active, setActive] = useState(true)
  const [medicine, setMedicine] = useState(emptyMedicine); const [reviewed, setReviewed] = useState(false)
  const [step, setStep] = useState(0)
  const [submittedCode, setSubmittedCode] = useState('')
  const priceInput = prices(price); const clinical = medicineInput(medicine)
  const valid = required(code, 64) && priceInput && (global || type && required(name, 256) && required(brand, 200) && barcode.trim().length <= 128 &&
    categories.some(row => row.id === categoryId && row.isActive) && (type === 'retail' || clinical))
  const identificationValid = Boolean(required(code, 64) && (global || type && required(name, 256)))
  const attributesValid = Boolean(global || required(brand, 200) && barcode.trim().length <= 128 && categories.some(row => row.id === categoryId && row.isActive) && (type === 'retail' || clinical))
  const saved = Boolean(submittedCode && write.notice && write.observed?.product.internalCode === submittedCode && !write.pending && !write.busy && write.error == null)
  return <form aria-label={global ? 'Asociar producto global' : 'Crear producto local'} onChange={onDirty} onSubmit={event => {
    event.preventDefault(); if (saved || write.locked || step !== 3 || !http || !valid || !reviewed || !priceInput) return
    setReviewed(false); setSubmittedCode(code.trim())
    const api = catalogApi(http)
    if (global) void write.run('global', undefined, async signal => (await api.fromGlobal(w, { globalProductId: global.id, internalCode: code.trim(), ...priceInput, isActive: active }, signal)).id, code.trim())
    else if (type) void write.run('create', undefined, async signal => (await api.create(w, { internalCode: code.trim(), name: name.trim(), productType: type, categoryId,
      brandOrLaboratory: brand.trim(), barcode: barcode.trim() || null, medicine: type === 'medicine' ? clinical : null, ...priceInput, isActive: active }, signal)).id, code.trim())
  }}>
    <Steps labels={['Identificación', 'Datos del producto', 'Precios', 'Revisión']} current={step} label="Pasos para crear el producto" />
    <fieldset disabled={write.locked || saved} onChange={() => setReviewed(false)}><legend>{global ? `Asociar: ${global.name}` : 'Nuevo producto de tu botica'}</legend>
      <div hidden={step !== 0} className="form-section-fields">
      <TextField label="Código interno" value={code} maxLength={64} required onChange={event => setCode(event.target.value)} />
      {!global && <>
        <label>Tipo de producto <select aria-label="Tipo de producto" required value={type} onChange={event => setType(event.target.value === 'medicine' ? 'medicine' : event.target.value === 'retail' ? 'retail' : '')}><option value="">Selecciona</option><option value="medicine">Medicamento</option><option value="retail">Venta general</option></select></label>
        <TextField label="Nombre del producto" value={name} maxLength={256} required onChange={event => setName(event.target.value)} />
      </>}
      {global && <p>{global.name}</p>}
      </div>
      <div hidden={step !== 1} className="form-section-fields">
      {!global && <>
        <label>Categoría <select aria-label="Categoría" value={categoryId} required onChange={event => setCategory(event.target.value)}><option value="">Selecciona una categoría existente</option>{categories.filter(row => row.isActive).map(row => <option key={row.id} value={row.id}>{row.name}</option>)}</select></label>
        {!categories.length && <p>No hay categorías disponibles. Su creación requiere administración independiente; esta pantalla no concede ese permiso.</p>}
        <TextField label="Marca o laboratorio documentado" value={brand} maxLength={200} required onChange={event => setBrand(event.target.value)} hint="Puede ser una marca verificable. No atribuyas el titular del registro como fabricante sin respaldo." />
        <TextField label="Código de barras (opcional)" value={barcode} maxLength={128} onChange={event => setBarcode(event.target.value)} />
        {type === 'medicine' && <MedicineFieldsForm value={medicine} change={setMedicine} disabled={write.locked} />}
      </>}
      {global && <><MedicineView value={global.medicine} /><p>Se copiarán los atributos del catálogo publicado al negocio. Vincular no crea stock físico.</p></>}
      </div>
      <div hidden={step !== 2} className="form-section-fields">
      <PriceInputs value={price} change={setPrice} disabled={write.locked} />
      <label><input type="checkbox" checked={active} onChange={event => setActive(event.target.checked)} /> Producto activo</label>
      </div>
      <div hidden={step !== 3} className="form-section-fields">
        <h3>{global?.name ?? name}</h3><p>Código interno: {code} · {active ? 'Activo' : 'Inactivo'}</p>
        <p>{global?.brandOrLaboratory ?? brand}</p>
        {priceInput && <p>Precio minorista por unidad base: S/ {decimalLabel(priceInput.retailPrice, 2)}. Mayorista: {priceInput.wholesalePrice ? `S/ ${decimalLabel(priceInput.wholesalePrice, 2)}` : 'Sin configurar'}.</p>}
        {type === 'medicine' && clinical && <p>{clinical.components.map(row => `${row.ingredient} ${row.strengthNormalized}`).join(' · ')} · {clinical.dosageForm}</p>}
        <p>Crear el producto no genera existencias. Después configura presentaciones y registra una recepción en Compras.</p>
      </div>
    </fieldset>
    <div hidden={step !== 3}>
    <label><input type="checkbox" checked={reviewed} disabled={write.locked || saved} onChange={event => setReviewed(event.target.checked)} /> Revisé la identidad, los atributos documentados y los precios de este producto.</label>
    </div>
    <FormFooter>
      {step > 0 && <Button variant="secondary" disabled={write.locked} onClick={() => setStep(step - 1)}>Anterior</Button>}
      {step < 3 ? <Button disabled={write.locked || (step === 0 ? !identificationValid : step === 1 ? !attributesValid : !priceInput)} onClick={() => setStep(step + 1)}>Continuar</Button>
        : <Button type="submit" pending={write.busy} disabled={write.locked || saved || !valid || !reviewed}>{saved ? 'Producto guardado' : global ? 'Asociar al negocio' : 'Guardar producto'}</Button>}
    </FormFooter>
    {write.notice && <p role="status">{write.notice}</p>}
  </form>
}
