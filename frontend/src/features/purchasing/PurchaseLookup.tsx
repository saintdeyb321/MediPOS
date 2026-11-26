import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiErrorState, isUuid, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { DateText } from '../../shared/ui/DateText'
import { Badge } from '../../shared/ui/Badge'
import { PageControls } from '../../shared/ui/PageControls'
import { purchasingApi } from './api'

export function PurchaseLookup({ consult, busy, fixedId }: { consult: (id: string) => void; busy: boolean; fixedId: string }) {
  const w = useWorkspace(); const { http } = useRuntime(); const [text, setText] = useState(''); const [reference, setReference] = useState('')
  const [id, setId] = useState(''); const [offset, setOffset] = useState(0)
  const result = useQuery({ queryKey: scopedKey(w, 'purchase-reference', w.session.userId, w.access.membershipId, reference, offset),
    queryFn: ({ signal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return purchasingApi(http).search(w, reference, offset, 20, signal) },
    enabled: Boolean(http && reference), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  return <section className="purchase-lookup" aria-label="Consultar recepción">
    <form className="page-toolbar-fields" onSubmit={event => {
      event.preventDefault()
      if (!text.trim() || !http || result.isFetching) return
      if (reference === text.trim() && offset === 0) void result.refetch()
      else { setOffset(0); setReference(text.trim()) }
    }}>
      <TextField label="Referencia de la recepción a consultar" placeholder="Número o referencia del comprobante" value={text} maxLength={256} onChange={event => setText(event.target.value)} />
      <Button type="submit" disabled={!http || !text.trim() || result.isFetching}>Buscar referencia</Button>
    </form>
    {result.isFetching && <p>Consultando recepciones…</p>}
    {result.isError && <ApiErrorState error={result.error} title="No pudimos consultar recepciones" retry={() => void result.refetch()} />}
    {!reference && <p className="data-caption">Busca por número o referencia del comprobante.</p>}
    {reference && result.data && !result.isError && <>
      {!result.data.length ? <p role="status">No hay coincidencias para esta referencia.</p>
        : <div className="operation-table" role="region" aria-label="Recepciones encontradas" tabIndex={0}><table>
          <thead><tr><th scope="col">Referencia</th><th scope="col">Fecha</th><th scope="col">Estado</th><th scope="col">Acción</th></tr></thead>
          <tbody>{result.data.map(row => <tr key={row.id}><td>{row.documentReference ?? 'Sin referencia'}</td><td><DateText value={row.createdAt} /></td>
            <td><Badge tone={row.status === 'confirmed' ? 'success' : 'info'}>{row.status === 'confirmed' ? 'Confirmada' : 'Borrador'}</Badge></td>
            <td><Button variant="quiet" aria-label={`Consultar recepción de ${row.createdAt}`} disabled={busy || Boolean(fixedId && fixedId.toLowerCase() !== row.id.toLowerCase())} onClick={() => consult(row.id)}>Abrir</Button></td>
          </tr>)}</tbody></table></div>}
      <PageControls offset={offset} count={result.data.length} busy={result.isFetching} change={setOffset} /></>}
    <details><summary>Buscar por código de soporte</summary><form onSubmit={event => { event.preventDefault(); consult(id.trim()) }}>
      <TextField label="Identificador de la recepción original" value={id} onChange={event => setId(event.target.value)} />
      <Button type="submit" disabled={busy || !isUuid(id.trim()) || Boolean(fixedId && fixedId.toLowerCase() !== id.trim().toLowerCase())}>Consultar identificador original</Button>
    </form></details>
  </section>
}
