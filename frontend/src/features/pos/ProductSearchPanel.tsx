import { useId, useState } from 'react'
import { Search } from 'lucide-react'
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

function ProductResult({ product, onSelect, disabled }: { product: ProductMatch; onSelect: (selection: ProductSelection) => void; disabled?: boolean }) {
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
    <li className="product-result">
      <div className="product-result-heading"><h3>{product.name}</h3><Badge tone={available ? 'success' : 'warning'}>{available ? 'Stock disponible' : 'Sin presentación vendible'}</Badge></div>
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
        <Button disabled={disabled || !selectable} aria-describedby={!selectable ? `${id}-availability` : undefined} onClick={() => { if (unit && quantity && selectable) onSelect({ product, unit, quantity }) }} aria-label={`Seleccionar ${product.name}`}>Seleccionar</Button>
      </div>
      {!selectable && <p id={`${id}-availability`}>No hay stock vendible suficiente para esta presentación o no hay presentaciones activas.</p>}
    </li>
  )
}

export function ProductSearchPanel({ onSelect, disabled }: { onSelect: (selection: ProductSelection) => void; disabled?: boolean }) {
  const [input, setInput] = useState('')
  const { query, ready, text, connected } = useProductSearch(input)
  const matches = query.data?.directMatches ?? []
  return (
    <Panel className="product-workspace" aria-label="Búsqueda y resultados">
      <div className="pos-search"><SearchField label="Buscar un producto" placeholder="Nombre, principio activo, laboratorio o código" maxLength={128} value={input}
        disabled={!connected || disabled} onChange={event => setInput(event.target.value)}
        hint={connected ? 'Busca en la sucursal seleccionada. También admite código de barras y tolerancia a errores del servidor.' : 'Se necesita una conexión real con MediPOS para buscar.'} /></div>
      <div className="product-results" aria-busy={Boolean(text) && (!ready || query.isFetching)}>
        <h2>Resultados</h2>
        {!text ? <EmptyState title="Busca productos de tu botica" description="Escribe un nombre, principio activo, laboratorio, código interno o código de barras." icon={<Search size={28} />} /> :
          !ready || query.isPending || query.isFetching ? <LoadingState label="Buscando productos…" /> :
          query.isError ? <ApiErrorState error={query.error} title="No pudimos buscar productos" retry={() => void query.refetch()} /> :
          matches.length === 0 ? <EmptyState title="Sin coincidencias en esta sucursal" description="No encontramos productos del negocio para esta búsqueda. Prueba otro nombre o código." /> :
          <><p className="product-count" role="status">{matches.length} coincidencias directas en esta sucursal</p>
            <ul className="product-list">{matches.map(product => <ProductResult key={product.businessProductId} product={product} onSelect={onSelect} disabled={disabled} />)}</ul></>}
      </div>
    </Panel>
  )
}
