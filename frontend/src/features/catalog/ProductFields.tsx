import { TextField } from '../../shared/ui/TextField'
import { Button } from '../../shared/ui/Button'
import type { Medicine } from './api'
import type { MedicineFields, PriceFields } from './forms'

export function PriceInputs({ value, change, disabled }: { value: PriceFields; change: (value: PriceFields) => void; disabled: boolean }) {
  return <>
    <TextField label="Precio minorista por unidad base (S/)" value={value.retail} inputMode="decimal" maxLength={32} required disabled={disabled} onChange={event => change({ ...value, retail: event.target.value })} />
    <TextField label="Precio mayorista por unidad base (S/, opcional)" value={value.wholesale} inputMode="decimal" maxLength={32} disabled={disabled} onChange={event => change({ ...value, wholesale: event.target.value })} />
    <p>Importes desde cero, hasta cuatro decimales. Vacío elimina el precio mayorista; no se redondean importes.</p>
  </>
}
export function MedicineFieldsForm({ value, change, disabled }: { value: MedicineFields; change: (value: MedicineFields) => void; disabled: boolean }) {
  return <fieldset disabled={disabled}><legend>Información farmacéutica documentada</legend>
    <p>Transcribe una fuente verificable. No inferimos dosis, fabricante, vigencia del registro ni equivalencias terapéuticas.</p>
    {value.components.map((component, index) => <div key={index} className="catalog-component">
      <TextField label={`Principio activo ${index + 1}`} value={component.ingredient} maxLength={200} required onChange={event => change({ ...value, components: value.components.map((row, position) => position === index ? { ...row, ingredient: event.target.value } : row) })} />
      <TextField label={`Concentración documentada ${index + 1}`} value={component.strengthNormalized} maxLength={128} required onChange={event => change({ ...value, components: value.components.map((row, position) => position === index ? { ...row, strengthNormalized: event.target.value } : row) })} />
      <Button variant="quiet" disabled={value.components.length === 1} onClick={() => change({ ...value, components: value.components.filter((_, position) => position !== index) })}>Quitar componente {index + 1}</Button>
    </div>)}
    <Button variant="secondary" onClick={() => change({ ...value, components: [...value.components, { ingredient: '', strengthNormalized: '' }] })}>Agregar componente</Button>
    <TextField label="Forma farmacéutica" value={value.dosageForm} maxLength={128} required onChange={event => change({ ...value, dosageForm: event.target.value })} />
    <TextField label="Vía (opcional)" value={value.route} maxLength={128} onChange={event => change({ ...value, route: event.target.value })} />
    <TextField label="Registro sanitario documentado (opcional)" value={value.sanitaryRegistration} maxLength={128} onChange={event => change({ ...value, sanitaryRegistration: event.target.value })} />
  </fieldset>
}
export function MedicineView({ value }: { value: Medicine | null }) {
  return value ? <section aria-label="Datos medicinales"><h3>Información medicinal</h3>
    {value.components.length ? <ul>{value.components.map((row, index) => <li key={index}>{row.ingredient} · {row.strengthNormalized}</li>)}</ul> : <p>Ingredientes: {value.activeIngredients.join(', ')} · Concentración declarada: {value.normalizedStrength}. Sin asociación estructurada verificable.</p>}
    <p>Forma: {value.dosageForm} · Vía: {value.route ?? 'No indicada'} · Registro documentado: {value.sanitaryRegistration ?? 'No indicado'}</p>
    <p>La presencia de registro no acredita su vigencia actual. Esta información no recomienda sustituciones terapéuticas.</p>
  </section> : null
}
