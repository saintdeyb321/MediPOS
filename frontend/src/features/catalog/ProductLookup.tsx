import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { ApiError } from '../../shared/api/http'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { catalogApi } from './api'
import type { ProductDetail } from './api'

// Purchases may select an active product with zero stock; no POS availability guard here.
function useProductDetail(id: string) {
  const w = useWorkspace(); const { http } = useRuntime()
  return useQuery({ queryKey: scopedKey(w, 'catalog-detail', w.session.userId, w.access.membershipId, id),
    queryFn: ({ signal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return catalogApi(http).detail(w, id, signal) },
    enabled: Boolean(http && id), retry: false, staleTime: 30_000, refetchOnWindowFocus: false })
}
export function ProductLabel({ id }: { id: string }) {
  const detail = useProductDetail(id)
  if (detail.isError) return <span>Nombre no disponible <Button variant="quiet" onClick={() => void detail.refetch()}>Consultar nombre</Button></span>
  return <span>{detail.data?.product.name ?? 'Consultando nombre…'}</span>
}
export function ProductLookup({ onSelect, disabled = false }: { onSelect: (detail: ProductDetail) => void; disabled?: boolean }) {
  const w = useWorkspace(); const { http } = useRuntime()
  const [text, setText] = useState(''); const [query, setQuery] = useState(''); const [selected, setSelected] = useState('')
  const results = useQuery({ queryKey: scopedKey(w, 'product-search', w.session.userId, w.access.membershipId, 'receipt-picker', query),
    queryFn: ({ signal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return catalogApi(http).search(w, query, 20, signal) },
    enabled: Boolean(http && query), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const detail = useProductDetail(selected)
  return <section aria-label="Buscar producto existente">
    <form onSubmit={event => { event.preventDefault(); setSelected(''); if (text.trim() === query) void results.refetch(); else setQuery(text.trim()) }}>
      <TextField label="Buscar producto por nombre, ingrediente o código" value={text} maxLength={256} onChange={event => setText(event.target.value)} disabled={disabled} />
      <Button type="submit" disabled={disabled || !http || text.trim().length < 2 || results.isFetching}>Buscar productos</Button>
    </form>
    {results.isFetching && <p role="status">Buscando productos…</p>}
    {results.isError && <ApiErrorState error={results.error} title="No pudimos buscar productos" />}
    {query && results.data && !results.isError && <><p>Coincidencias directas, hasta 20; afina el texto si falta el producto. No se seleccionan equivalencias automáticamente.</p>
      {!results.data.directMatches.length && <p>No hay coincidencias directas. No se crearon productos.</p>}
      <ul>{results.data.directMatches.map(row => <li key={row.businessProductId}>{row.name} · {row.internalCode} <Button variant="quiet" disabled={disabled} onClick={() => setSelected(row.businessProductId)}>Consultar {row.name}</Button></li>)}</ul></>}
    {selected && detail.isFetching && <p>Consultando producto…</p>}
    {selected && detail.isError && <ApiErrorState error={detail.error} retry={() => void detail.refetch()} title="No pudimos consultar el producto" />}
    {detail.data && !detail.isError && !detail.isFetching && <><p>{detail.data.product.name} · {detail.data.product.internalCode}</p>
      <Button disabled={disabled || !detail.data.product.isActive} onClick={() => onSelect(detail.data)}>Usar producto consultado</Button></>}
  </section>
}
