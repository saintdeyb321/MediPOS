import { useState } from 'react'
import { useQueries } from '@tanstack/react-query'
import { catalogApi } from '../catalog'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { decimalLabel } from '../../shared/api/decimal'
import { ApiError } from '../../shared/api/http'
import { Button } from '../../shared/ui/Button'
import type { PurchaseView } from './api'
import { receiptTotal } from './receipt'

export function PurchaseReview({ view, disabled, confirm }: { view: PurchaseView; disabled: boolean; confirm: () => void }) {
  const w = useWorkspace(); const { http } = useRuntime(); const [reviewed, setReviewed] = useState(false)
  const ids = [...new Set(view.lines.map(row => row.businessProductId))]
  const products = useQueries({ queries: ids.map(id => ({ queryKey: scopedKey(w, 'catalog-detail', w.session.userId, w.access.membershipId, id),
    queryFn: ({ signal }: { signal: AbortSignal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return catalogApi(http).detail(w, id, signal) },
    enabled: Boolean(http), retry: false, staleTime: 30_000, refetchOnWindowFocus: false })) })
  const ready = view.lines.length > 0 && products.every(row => row.isSuccess && !row.isError) && view.lines.every(line => {
    const product = products[ids.indexOf(line.businessProductId)]?.data?.product
    return product?.isActive && (product.productType !== 'medicine' || Boolean(line.batchNumber?.trim() && line.expirationDate))
  })
  return <section aria-label="Recepción persistida"><h2>{view.purchase.status === 'confirmed' ? 'MERCADERÍA REGISTRADA' : 'Revisar recepción persistida'}</h2>
    <p>Referencia: {view.purchase.documentReference ?? 'Sin referencia'} · Estado: {view.purchase.status === 'confirmed' ? 'Confirmada' : 'Borrador'} · Creada {view.purchase.createdAt}</p>
    <p>{view.purchase.supplierId ? 'Proveedor registrado: localízalo en el directorio paginado; aparecerá marcado como proveedor de esta recepción.' : 'Recepción sin proveedor registrado.'}</p>
    {view.purchase.confirmedAt && <p>Confirmación del servidor: {view.purchase.confirmedAt}. No repetir la recepción.</p>}
    {products.some(row => row.isError) && <ApiErrorState error={products.find(row => row.isError)?.error} title="No pudimos verificar los productos de la recepción" />}
    <div className="operation-table"><table><caption>Líneas consultadas en el servidor</caption><thead><tr><th>Producto / presentación guardada</th><th>Cantidad / base</th><th>Costo por presentación</th><th>Lote / vencimiento</th></tr></thead><tbody>{view.lines.map(line => <tr key={line.id}><td>{products[ids.indexOf(line.businessProductId)]?.data?.product.name ?? 'Nombre no disponible'} · {line.unitNameSnapshot}</td><td>{decimalLabel(line.quantity)} / {decimalLabel(line.baseQuantity)}</td><td>S/ {decimalLabel(line.unitCost, 2)}</td><td>{line.batchNumber ?? 'No informado'} / {line.expirationDate ?? 'No informado'}</td></tr>)}</tbody></table></div>
    {!view.lines.length && <p>La recepción todavía no tiene líneas persistidas.</p>}
    <p>Importe documental calculado desde las líneas guardadas: S/ {decimalLabel(receiptTotal(view.lines), 2)}. La API no devuelve un total independiente.</p>
    {view.purchase.status === 'draft' && <><p>Confirmar genera lotes y movimientos de stock de forma atómica en el servidor. Evita editar esta recepción desde otra pestaña u operador mientras la revisas; el backend vuelve a verificar permisos y productos.</p>
      {!ready && <p>Falta verificar productos activos o completar lote/vencimiento de medicamentos antes de confirmar.</p>}
      <label><input type="checkbox" checked={reviewed} disabled={disabled || !ready} onChange={event => setReviewed(event.target.checked)} /> Revisé el estado persistido y recibí físicamente esta mercadería.</label>
      <Button disabled={disabled || !ready || !reviewed} onClick={confirm}>Confirmar recepción una vez</Button>
    </>}
  </section>
}
