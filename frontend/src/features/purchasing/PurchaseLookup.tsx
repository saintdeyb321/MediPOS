import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiErrorState, isUuid, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { PageControls } from '../../shared/ui/PageControls'
import { purchasingApi } from './api'

export function PurchaseLookup({ consult, busy, fixedId }: { consult: (id: string) => void; busy: boolean; fixedId: string }) {
  const w = useWorkspace(); const { http } = useRuntime(); const [text, setText] = useState(''); const [reference, setReference] = useState('')
  const [id, setId] = useState(''); const [offset, setOffset] = useState(0)
  const result = useQuery({ queryKey: scopedKey(w, 'purchase-reference', w.session.userId, w.access.membershipId, reference, offset),
    queryFn: ({ signal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return purchasingApi(http).search(w, reference, offset, 20, signal) },
    enabled: Boolean(http && reference), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  return <section aria-label="Consultar recepción"><h2>Consultar recepción existente</h2>
    <p>La API permite buscar una referencia documental o consultar un identificador conocido; no ofrece un listado general. Una referencia no es única ni una clave de idempotencia.</p>
    <form onSubmit={event => { event.preventDefault(); setOffset(0); if (reference === text.trim()) void result.refetch(); else setReference(text.trim()) }}>
      <TextField label="Referencia de la recepción a consultar" value={text} maxLength={256} onChange={event => setText(event.target.value)} />
      <Button type="submit" disabled={!http || !text.trim() || result.isFetching}>Buscar referencia</Button>
    </form>
    {result.isFetching && <p>Consultando recepciones…</p>}
    {result.isError && <ApiErrorState error={result.error} title="No pudimos consultar recepciones" />}
    {reference && result.data && !result.isError && <><ul>{!result.data.length && <li>No hay coincidencias en esta página. Esto no demuestra que una creación pendiente haya fallado.</li>}{result.data.map(row => <li key={row.id}>{row.documentReference ?? 'Sin referencia'} · {row.createdAt} · {row.status === 'confirmed' ? 'Confirmada' : 'Borrador'} <Button variant="quiet" disabled={busy || Boolean(fixedId && fixedId.toLowerCase() !== row.id.toLowerCase())} onClick={() => consult(row.id)}>Consultar recepción de {row.createdAt}</Button></li>)}</ul>
      <PageControls offset={offset} count={result.data.length} busy={result.isFetching} change={setOffset} refresh={() => void result.refetch()} /></>}
    <details><summary>Recuperación excepcional con identificador conocido</summary><form onSubmit={event => { event.preventDefault(); consult(id.trim()) }}>
      <TextField label="Identificador de la recepción original" value={id} onChange={event => setId(event.target.value)} />
      <Button type="submit" disabled={busy || !isUuid(id.trim()) || Boolean(fixedId && fixedId.toLowerCase() !== id.trim().toLowerCase())}>Consultar identificador original</Button>
    </form></details>
  </section>
}
