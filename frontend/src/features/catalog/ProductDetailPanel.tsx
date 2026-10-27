import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Notice } from '../../shared/ui/Notice'
import { FormFooter } from '../../shared/ui/FormLayout'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { ApiError } from '../../shared/api/http'
import { decimalLabel, multiply } from '../../shared/api/decimal'
import { catalogApi } from './api'
import type { ProductDetail, UnitConfiguration } from './api'
import { prices, unitInputs } from './forms'
import type { UnitFields } from './forms'
import { MedicineView, PriceInputs } from './ProductFields'
import type { CatalogWrite } from './useCatalogWrite'

export type ProductTask = 'prices' | 'status' | 'units' | 'metadata'
export function ProductDetailPanel({ detail, write, onEdit }: { detail: ProductDetail; write: CatalogWrite; onEdit: (task: ProductTask) => void }) {
  const w = useWorkspace()
  const [tab, setTab] = useState<'data' | 'prices' | 'units' | 'stock'>('data')
  const p = detail.product
  return <section aria-label="Detalle del producto">
    {!p.globalProductId && <Button disabled={write.locked} onClick={() => onEdit('metadata')}>Corregir datos del producto local</Button>}
    <h2>{p.name}</h2><p>Código: {p.internalCode} · {p.productType === 'medicine' ? 'Medicamento' : 'Venta general'} · {p.isActive ? 'Activo' : 'Inactivo'}</p>
    <nav className="task-tabs" aria-label="Detalle del producto">{([['data', 'Datos'], ['prices', 'Precios'], ['units', 'Presentaciones'], ['stock', 'Lotes y stock']] as const).map(([id, label]) => <Button key={id} variant="quiet" aria-pressed={tab === id} onClick={() => setTab(id)}>{label}</Button>)}</nav>
    {tab === 'data' && <><p>Marca/laboratorio: {p.brandOrLaboratory} · Barras: {p.barcode ?? 'No indicado'} · Origen: {p.globalProductId ? 'Vinculado desde catálogo global' : 'Local del negocio'}</p>
    <MedicineView value={p.medicine} /><Button variant="secondary" disabled={write.locked} onClick={() => onEdit('status')}>{p.isActive ? 'Desactivar producto' : 'Activar producto'}</Button></>}
    {tab === 'prices' && <><dl className="summary-grid"><div><dt>Minorista · unidad base</dt><dd>S/ {decimalLabel(p.retailPrice, 2)}</dd></div><div><dt>Mayorista · unidad base</dt><dd>{p.wholesalePrice ? `S/ ${decimalLabel(p.wholesalePrice, 2)}` : 'Sin configurar'}</dd></div></dl><Button disabled={write.locked} onClick={() => onEdit('prices')}>Editar precios</Button></>}
    {tab === 'units' && <>
    <h3>Presentaciones configuradas</h3>
    {!detail.units.length ? <p>Aún no tiene presentaciones.</p> : <ul>{detail.units.map(unit => <li key={unit.id}>{unit.name} · Conversión exacta: {decimalLabel(unit.conversionToBase)} · {unit.isBaseUnit ? 'Unidad base' : 'Presentación comercial'} · {unit.isActive ? 'Activa' : 'Inactiva'}
      <p>Precio minorista por presentación: S/ {decimalLabel(multiply(p.retailPrice, unit.conversionToBase), 2)}{p.wholesalePrice && ` · Mayorista: S/ ${decimalLabel(multiply(p.wholesalePrice, unit.conversionToBase), 2)}`}</p>
    </li>)}</ul>}
    <Button disabled={write.locked} onClick={() => onEdit('units')}>Configurar presentaciones</Button></>}
    {tab === 'stock' && <><p>Las existencias y los vencimientos pertenecen a los lotes de cada sucursal.</p><Link className="button secondary" to={w.base + '/inventario'}>Consultar inventario de {w.branch.name}</Link><Link className="text-link" to={w.base + '/compras'}>Registrar recepción en Compras</Link></>}
  </section>
}
export function EditPrices({ detail, write }: { detail: ProductDetail; write: CatalogWrite }) {
  const w = useWorkspace(); const { http } = useRuntime(); const p = detail.product
  const [value, setValue] = useState({ retail: decimalLabel(p.retailPrice), wholesale: p.wholesalePrice ? decimalLabel(p.wholesalePrice) : '' })
  const [reviewed, setReviewed] = useState(false); const input = prices(value)
  return <form aria-label="Actualizar precios" onSubmit={event => { event.preventDefault(); if (http && input && reviewed) void write.run('prices', p.id, async signal => (await catalogApi(http).prices(w, p.id, input, signal)).id) }}>
    <h3>Precios base del negocio</h3>
    <PriceInputs value={value} change={next => { setValue(next); setReviewed(false) }} disabled={write.locked} />
    <label><input type="checkbox" checked={reviewed} disabled={write.locked} onChange={event => setReviewed(event.target.checked)} /> Confirmo los nuevos precios base.</label>
    <FormFooter><Button type="submit" disabled={write.locked || !input || !reviewed}>Guardar precios</Button></FormFooter>
  </form>
}
export function EditStatus({ detail, write }: { detail: ProductDetail; write: CatalogWrite }) {
  const w = useWorkspace(); const { http } = useRuntime(); const p = detail.product
  const [reviewed, setReviewed] = useState(false)
  return <form aria-label="Cambiar estado" onSubmit={event => { event.preventDefault(); if (http && reviewed) void write.run('status', p.id, async signal => (await catalogApi(http).status(w, p.id, !p.isActive, signal)).id) }}>
    <p>Desactivar retira el producto de nuevas operaciones; no elimina su historial.</p>
    <label><input type="checkbox" checked={reviewed} disabled={write.locked} onChange={event => setReviewed(event.target.checked)} /> Confirmo {p.isActive ? 'desactivar' : 'activar'} este producto.</label>
    <FormFooter><Button type="submit" variant={p.isActive ? 'danger' : 'primary'} disabled={write.locked || !reviewed}>{p.isActive ? 'Desactivar producto' : 'Activar producto'}</Button></FormFooter>
  </form>
}
export function UnitEditor({ detail, write }: { detail: ProductDetail; write: CatalogWrite }) {
  const w = useWorkspace(); const { http } = useRuntime()
  const configuration = useQuery({ queryKey: scopedKey(w, 'unit-configuration', w.session.userId, w.access.membershipId, detail.product.id),
    queryFn: ({ signal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return catalogApi(http).unitConfiguration(w, detail.product.id, signal) },
    enabled: Boolean(http), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  return <section aria-label="Configuración versionada de presentaciones"><h3>Editar presentaciones</h3>
    <p>Las unidades existentes conservan su identidad y factor. Para otro factor agrega una presentación; para retirarla de nuevas operaciones desactívala.</p>
    {configuration.isPending && <p role="status">Consultando configuración vigente…</p>}
    {configuration.isError && <ApiErrorState error={configuration.error} title="No pudimos consultar la configuración de presentaciones" />}
    {configuration.data && !configuration.isError && <UnitConfigurationForm key={configuration.data.version} productId={detail.product.id} configuration={configuration.data} write={write} />}
    <Button variant="quiet" disabled={configuration.isFetching || write.busy} onClick={() => void configuration.refetch()}>Recargar presentaciones del servidor</Button>
  </section>
}
function UnitConfigurationForm({ productId, configuration, write }: { productId: string; configuration: UnitConfiguration; write: CatalogWrite }) {
  const w = useWorkspace(); const { http } = useRuntime(); const [reviewed, setReviewed] = useState(false)
  const [rows, setRows] = useState<UnitFields[]>(() => configuration.units.length ? configuration.units.map(unit => ({ id: unit.id, name: unit.name, conversion: decimalLabel(unit.conversionToBase), base: unit.isBaseUnit, active: unit.isActive })) : [{ name: '', conversion: '1', base: true, active: true }])
  const input = unitInputs(rows)
  function change(index: number, patch: Partial<UnitFields>) { setReviewed(false); setRows(rows.map((row, position) => position === index ? { ...row, ...patch } : patch.base ? { ...row, base: false } : row)) }
  return <form aria-label="Guardar configuración de presentaciones" onSubmit={event => { event.preventDefault(); if (http && input && reviewed) void write.run('units', productId, async signal => { await catalogApi(http).configureUnits(w, productId, configuration.version, input.map((unit, index) => ({ ...unit, id: rows[index]?.id ?? null })), signal); return productId }) }}>
    <Notice tone="info">Se guarda el conjunto completo con la versión consultada. Un conflicto requiere recargar y revisar; nunca se reenvía automáticamente.</Notice>
    <fieldset disabled={write.locked}>{rows.map((row, index) => <div key={index}>
      <TextField label={`Nombre de presentación ${index + 1}`} value={row.name} required maxLength={128} onChange={event => change(index, { name: event.target.value })} />
      <TextField label={`Conversión a base ${index + 1}`} value={row.conversion} inputMode="decimal" required maxLength={40} disabled={Boolean(row.id) || row.base} onChange={event => change(index, { conversion: event.target.value })} />
      <label><input type="radio" name="base-unit" checked={row.base} disabled={configuration.units.length > 0} onChange={() => change(index, { base: true, conversion: '1' })} /> Unidad base {index + 1}</label>
      <label><input type="checkbox" checked={row.active} onChange={event => change(index, { active: event.target.checked })} /> Presentación activa {index + 1}</label>
      <Button variant="quiet" disabled={Boolean(row.id) || row.base} onClick={() => { setReviewed(false); setRows(rows.filter((_, position) => position !== index)) }}>Quitar presentación nueva {index + 1}</Button>
    </div>)}<Button variant="secondary" disabled={rows.length >= 100} onClick={() => { setReviewed(false); setRows([...rows, { name: '', conversion: '', base: false, active: true }]) }}>Agregar presentación</Button></fieldset>
    {!input && <p role="status">Revisa nombres únicos, exactamente una base con factor uno y conversiones positivas de hasta doce decimales.</p>}
    <label><input type="checkbox" checked={reviewed} disabled={write.locked} onChange={event => setReviewed(event.target.checked)} /> Revisé nombres, actividad y el conjunto completo; los factores existentes se conservan.</label>
    <FormFooter><Button type="submit" disabled={write.locked || !input || !reviewed}>Guardar presentaciones</Button></FormFooter>
  </form>
}
