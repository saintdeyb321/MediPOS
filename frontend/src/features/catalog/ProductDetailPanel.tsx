import { useState } from 'react'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Notice } from '../../shared/ui/Notice'
import { useRuntime, useWorkspace } from '../access'
import { decimalLabel, multiply } from '../../shared/api/decimal'
import { catalogApi } from './api'
import type { ProductDetail } from './api'
import { prices, unitInputs } from './forms'
import type { UnitFields } from './forms'
import { MedicineView, PriceInputs } from './ProductFields'
import type { CatalogWrite } from './useCatalogWrite'

export function ProductDetailPanel({ detail, write }: { detail: ProductDetail; write: CatalogWrite }) {
  const p = detail.product
  return <section aria-label="Detalle del producto">
    <h2>{p.name}</h2><p>Código: {p.internalCode} · {p.productType === 'medicine' ? 'Medicamento' : 'Venta general'} · {p.isActive ? 'Activo' : 'Inactivo'}</p>
    <p>Marca/laboratorio: {p.brandOrLaboratory} · Barras: {p.barcode ?? 'No indicado'} · Origen: {p.globalProductId ? 'Vinculado desde catálogo global' : 'Local del negocio'}</p>
    <MedicineView value={p.medicine} />
    <h3>Presentaciones configuradas</h3>
    {!detail.units.length ? <p>Aún no tiene presentaciones.</p> : <ul>{detail.units.map(unit => <li key={unit.id}>{unit.name} · Conversión exacta: {decimalLabel(unit.conversionToBase)} · {unit.isBaseUnit ? 'Unidad base' : 'Presentación comercial'} · {unit.isActive ? 'Activa' : 'Inactiva'}
      <p>Precio minorista por presentación: S/ {decimalLabel(multiply(p.retailPrice, unit.conversionToBase), 2)}{p.wholesalePrice && ` · Mayorista: S/ ${decimalLabel(multiply(p.wholesalePrice, unit.conversionToBase), 2)}`}</p>
    </li>)}</ul>}
    <p>Este detalle no devuelve existencias ni disponibilidad física por sucursal.</p>
    <EditPrices key={`${p.id}/${p.retailPrice.literal}/${p.wholesalePrice?.literal}`} detail={detail} write={write} />
    <EditStatus key={`${p.id}/${p.isActive}`} detail={detail} write={write} />
    <UnitEditor key={`${p.id}/${detail.units.map(row => row.id).join('/')}`} detail={detail} write={write} />
  </section>
}
function EditPrices({ detail, write }: { detail: ProductDetail; write: CatalogWrite }) {
  const w = useWorkspace(); const { http } = useRuntime(); const p = detail.product
  const [value, setValue] = useState({ retail: decimalLabel(p.retailPrice), wholesale: p.wholesalePrice ? decimalLabel(p.wholesalePrice) : '' })
  const [reviewed, setReviewed] = useState(false); const input = prices(value)
  return <form aria-label="Actualizar precios" onSubmit={event => { event.preventDefault(); if (http && input && reviewed) void write.run('prices', p.id, async signal => (await catalogApi(http).prices(w, p.id, input, signal)).id) }}>
    <h3>Precios base del negocio</h3>
    <PriceInputs value={value} change={next => { setValue(next); setReviewed(false) }} disabled={write.locked} />
    <label><input type="checkbox" checked={reviewed} disabled={write.locked} onChange={event => setReviewed(event.target.checked)} /> Confirmo los nuevos precios base.</label>
    <Button type="submit" disabled={write.locked || !input || !reviewed}>Guardar precios</Button>
  </form>
}
function EditStatus({ detail, write }: { detail: ProductDetail; write: CatalogWrite }) {
  const w = useWorkspace(); const { http } = useRuntime(); const p = detail.product
  const [reviewed, setReviewed] = useState(false)
  return <form aria-label="Cambiar estado" onSubmit={event => { event.preventDefault(); if (http && reviewed) void write.run('status', p.id, async signal => (await catalogApi(http).status(w, p.id, !p.isActive, signal)).id) }}>
    <p>Desactivar retira el producto de nuevas operaciones; no elimina su historial.</p>
    <label><input type="checkbox" checked={reviewed} disabled={write.locked} onChange={event => setReviewed(event.target.checked)} /> Confirmo {p.isActive ? 'desactivar' : 'activar'} este producto.</label>
    <Button type="submit" variant="secondary" disabled={write.locked || !reviewed}>{p.isActive ? 'Desactivar producto' : 'Activar producto'}</Button>
  </form>
}
function UnitEditor({ detail, write }: { detail: ProductDetail; write: CatalogWrite }) {
  const w = useWorkspace(); const { http } = useRuntime(); const [reviewed, setReviewed] = useState(false)
  const [rows, setRows] = useState<UnitFields[]>([{ name: '', conversion: '1', base: true, active: true }])
  const input = unitInputs(rows)
  function change(index: number, patch: Partial<UnitFields>) { setReviewed(false); setRows(rows.map((row, position) => position === index ? { ...row, ...patch } : patch.base ? { ...row, base: false } : row)) }
  if (detail.units.length) return <Notice tone="warning">Reconfiguración bloqueada: el endpoint reemplaza el conjunto completo y cambia sus identificadores. Podría invalidar ventas/compras pendientes que usan esas presentaciones. Aquí se conservan íntegramente; hace falta un contrato backend seguro para editar, desactivar o retirar unidades existentes.</Notice>
  return <form aria-label="Configurar presentaciones iniciales" onSubmit={event => { event.preventDefault(); if (http && input && reviewed) void write.run('units', detail.product.id, async signal => { await catalogApi(http).initializeUnits(w, detail.product.id, input, signal); return detail.product.id }) }}>
    <h3>Configuración inicial de presentaciones</h3><Notice tone="warning">Guardado bloqueado: el PUT existente borra y recrea el conjunto. Ni siquiera consultar primero un conjunto vacío garantiza que otra sesión no lo configure antes. Se necesita una precondición atómica y una estrategia de identificadores estables en el backend. Puedes revisar la definición aquí sin enviar cambios.</Notice>
    <fieldset disabled={write.locked}>{rows.map((row, index) => <div key={index}>
      <TextField label={`Nombre de presentación ${index + 1}`} value={row.name} required maxLength={128} onChange={event => change(index, { name: event.target.value })} />
      <TextField label={`Conversión a base ${index + 1}`} value={row.conversion} inputMode="decimal" required maxLength={40} disabled={row.base} onChange={event => change(index, { conversion: event.target.value })} />
      <label><input type="radio" name="base-unit" checked={row.base} onChange={() => change(index, { base: true, conversion: '1' })} /> Unidad base {index + 1}</label>
      <label><input type="checkbox" checked={row.active} onChange={event => change(index, { active: event.target.checked })} /> Presentación activa {index + 1}</label>
      <Button variant="quiet" disabled={row.base} onClick={() => { setReviewed(false); setRows(rows.filter((_, position) => position !== index)) }}>Quitar presentación {index + 1}</Button>
    </div>)}<Button variant="secondary" onClick={() => { setReviewed(false); setRows([...rows, { name: '', conversion: '', base: false, active: true }]) }}>Agregar presentación</Button></fieldset>
    {!input && <p role="status">Revisa nombres únicos, exactamente una base con factor uno y conversiones positivas de hasta doce decimales.</p>}
    <label><input type="checkbox" checked={reviewed} disabled={write.locked} onChange={event => setReviewed(event.target.checked)} /> Revisé el conjunto completo; no hay otra configuración u operación en curso con este producto.</label>
    <Button type="submit" disabled>Guardar presentaciones iniciales (bloqueado por contrato)</Button>
  </form>
}
