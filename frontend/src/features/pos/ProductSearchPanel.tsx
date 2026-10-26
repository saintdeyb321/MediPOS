import { useEffect, useId, useState } from 'react'
import { Pill, Package, Plus } from 'lucide-react'
import type { ProductMatch } from '../catalog'
import { ApiErrorState } from '../access'
import { Panel } from '../../shared/ui/Panel'
import { SearchField } from '../../shared/ui/TextField'
import { EmptyState, LoadingState } from '../../shared/ui/States'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { canSelectQuantity, canSelectUnit, decimalLabel } from './productSelection'
import { decimalInput, maximumQuantity } from '../../shared/api/decimal'
import { TextField } from '../../shared/ui/TextField'
import type { ProductSelection } from './productSelection'
import { useProductSearch } from './useProductSearch'
import { PosCatalogBrowser } from './PosCatalogBrowser'
import { multiply } from '../../shared/api/decimal'
import { Modal } from '../../shared/ui/Modal'

function ProductChoice({ product, onSelect, disabled }: { product: ProductMatch; onSelect: (selection: ProductSelection) => void; disabled?: boolean }) {
  const id = useId()
  const [unitId, setUnitId] = useState('')
  const [quantityText, setQuantityText] = useState('1')
  const quantity = decimalInput(quantityText, maximumQuantity, 12)
  const units = product.units.filter(unit => unit.isActive)
  const unit = units.find(item => item.id === unitId) ??
    units.find(item => item.isBaseUnit && canSelectUnit(product, item)) ?? units.find(item => canSelectUnit(product, item)) ?? units[0]
  const selectable = Boolean(unit && quantity && canSelectQuantity(product, unit, quantity))
  const available = units.some(item => canSelectUnit(product, item))
  return (
    <div className="product-choice">
      <div className="product-result-heading"><span className="product-placeholder">{product.productType === 'medicine' ? <Pill size={26} aria-hidden="true" /> : <Package size={26} aria-hidden="true" />}</span><h3>{product.name}</h3><Badge tone={available ? 'success' : 'warning'}>{available ? 'Stock disponible' : 'Sin presentación vendible'}</Badge></div>
      <p>{product.brandOrLaboratory} · Código: {product.internalCode}</p>
      {product.barcode && <p>Código de barras: {product.barcode}</p>}
      <dl className="product-facts">
        <div><dt>Precio por unidad base</dt><dd>S/ {decimalLabel(product.retailPrice, 2)}</dd></div>
        <div><dt>Stock vendible en unidades base</dt><dd>{decimalLabel(product.quantityAvailableBase)}</dd></div>
      </dl>
      <div className="product-selection">
        <div className="form-field"><label htmlFor={id}>Presentación de {product.name}</label>
          <select id={id} value={unit?.id ?? ''} disabled={disabled || units.length === 0} onChange={event => setUnitId(event.target.value)}>
            {units.length === 0 && <option value="">Sin presentaciones activas</option>}
            {units.map(item => <option key={item.id} value={item.id}>{item.name} · {decimalLabel(item.conversionToBase)} unidades base</option>)}
          </select>
        </div>
        <TextField label={`Cantidad a agregar de ${product.name}`} value={quantityText} inputMode="decimal" maxLength={32} disabled={disabled}
          onChange={event => setQuantityText(event.target.value)} error={!quantity ? 'Cantidad positiva, hasta 12 decimales.' : undefined} />
        <Button disabled={disabled || !selectable} aria-describedby={!selectable ? `${id}-availability` : undefined} onClick={() => { if (unit && quantity && selectable) onSelect({ product, unit, quantity }) }} aria-label={`Seleccionar ${product.name}`}><Plus size={18} aria-hidden="true" />Agregar</Button>
      </div>
      {unit && <p className="selection-price">S/ {decimalLabel(multiply(product.retailPrice, unit.conversionToBase), 2)} por {unit.name} · {decimalLabel(unit.conversionToBase)} unidades base</p>}
      {!selectable && <p id={`${id}-availability`}>No hay stock vendible suficiente para esta presentación o no hay presentaciones activas.</p>}
    </div>
  )
}

export function ProductSearchPanel({ onSelect, disabled }: { onSelect: (selection: ProductSelection) => void; disabled?: boolean }) {
  const [input, setInput] = useState('')
  const searchId = useId()
  useEffect(() => {
    const frame = window.requestAnimationFrame(() => {
      const field = document.getElementById(searchId)
      if (field && !field.matches(':disabled') && !document.querySelector('dialog[open]')) field.focus({ preventScroll: true })
    })
    return () => window.cancelAnimationFrame(frame)
  }, [searchId])
  const [choice, setChoice] = useState<ProductMatch | null>(null)
  const { query, ready, text, connected } = useProductSearch(input)
  const matches = query.data?.directMatches ?? []
  return (
    <Panel className="product-workspace" aria-label="Búsqueda y resultados">
      <div className="pos-search"><SearchField id={searchId} label="Buscar producto" placeholder="Nombre, principio activo o código de barras" maxLength={128} value={input}
        disabled={!connected || disabled} onChange={event => setInput(event.target.value)}
        hint={!connected ? 'Requiere conexión HTTPS.' : undefined} /></div>
      <div className="product-results" aria-busy={Boolean(text) && (!ready || query.isFetching)}>
        {!text ? <PosCatalogBrowser search={setInput} disabled={disabled} /> :
          !ready || query.isPending || query.isFetching ? <LoadingState label="Buscando productos…" /> :
          query.isError ? <ApiErrorState error={query.error} title="No pudimos buscar productos" retry={() => void query.refetch()} /> :
          matches.length === 0 ? <EmptyState title="Sin coincidencias en esta sucursal" description="No encontramos productos del negocio para esta búsqueda. Prueba otro nombre o código." /> :
          <><p className="product-count" role="status">{matches.length} coincidencias directas en esta sucursal</p>
            <ul className="product-list">{matches.map(product => <li key={product.businessProductId} className="product-tile">
              <span className="product-placeholder">{product.productType === 'medicine' ? <Pill size={22} aria-hidden="true" /> : <Package size={22} aria-hidden="true" />}</span><h3>{product.name}</h3><small>{product.brandOrLaboratory}</small>
              <strong>S/ {decimalLabel(product.retailPrice, 2)} <small>/ base</small></strong><span className="data-caption">Stock vendible: {decimalLabel(product.quantityAvailableBase)} base</span>
              <Button variant="secondary" disabled={disabled} onClick={() => setChoice(product)}>Elegir presentación</Button>
            </li>)}</ul></>}
      </div>
      <Modal open={choice !== null} title="Agregar al ticket" description={choice?.name} onClose={() => setChoice(null)}>
        {choice && <ProductChoice key={choice.businessProductId} product={choice} disabled={disabled} onSelect={selection => { onSelect(selection); setChoice(null) }} />}
      </Modal>
    </Panel>
  )
}
