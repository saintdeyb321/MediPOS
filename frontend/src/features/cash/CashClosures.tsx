import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { decimalLabel } from '../../shared/api/decimal'
import { Button } from '../../shared/ui/Button'
import { DataSection } from '../../shared/ui/DataSection'
import { DateText } from '../../shared/ui/DateText'
import { PageControls } from '../../shared/ui/PageControls'
import { TextField } from '../../shared/ui/TextField'
import { cashApi } from './api'

function defaultPeriod() {
  const parts = new Intl.DateTimeFormat('en', { timeZone: 'America/Lima', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts()
  const to = ['year', 'month', 'day'].map(type => parts.find(part => part.type === type)!.value).join('-')
  const from = new Date(to + 'T00:00:00Z')
  from.setUTCDate(from.getUTCDate() - 29)
  return { from: from.toISOString().slice(0, 10), to }
}

export function CashClosures({ blocked, select }: { blocked: boolean; select: (id: string) => void }) {
  const w = useWorkspace(); const { http } = useRuntime()
  const [period, setPeriod] = useState(defaultPeriod)
  const [draft, setDraft] = useState(period)
  const [offset, setOffset] = useState(0)
  const [invalid, setInvalid] = useState(false)
  const history = useQuery({
    queryKey: scopedKey(w, 'closed-cash', w.session.userId, w.access.membershipId, w.access.role, period.from, period.to, offset),
    enabled: Boolean(http && !blocked), staleTime: 0, retry: false,
    queryFn: ({ signal }) => cashApi(http!).closed(w, period.from, period.to, offset, signal),
  })
  const page = history.isSuccess ? history.data : undefined
  return <DataSection tabs={<h2>Cierres de caja</h2>}
    footer={<PageControls offset={offset} busy={blocked || history.isFetching} size={50} count={page?.hasMore ? 50 : 0} change={setOffset} />}>
    <form className="operation-actions" onSubmit={event => {
      event.preventDefault()
      if (blocked) return
      const days = (Date.parse(draft.to) - Date.parse(draft.from)) / 86_400_000
      setInvalid(!Number.isInteger(days) || days < 0 || days >= 90)
      if (!Number.isInteger(days) || days < 0 || days >= 90) return
      if (period.from === draft.from && period.to === draft.to && offset === 0) void history.refetch()
      else { setOffset(0); setPeriod(draft) }
    }}>
      <TextField label="Desde (Lima)" type="date" value={draft.from} required disabled={blocked} onChange={event => setDraft({ ...draft, from: event.target.value })} />
      <TextField label="Hasta (Lima)" type="date" value={draft.to} required disabled={blocked} onChange={event => setDraft({ ...draft, to: event.target.value })} />
      <Button disabled={blocked || !http} type="submit">Consultar cierres</Button>
    </form>
    {invalid && <p role="alert">Selecciona un intervalo de hasta 90 días calendario.</p>}
    {!http && <p>La consulta de cierres requiere conexión con MediPOS.</p>}
    {history.isFetching && <p role="status">Consultando cierres…</p>}
    {history.isError && <ApiErrorState error={history.error} title="No pudimos consultar los cierres" retry={blocked ? undefined : () => void history.refetch()} />}
    {page?.items.length === 0 && <p>No hay cierres para este intervalo y página.</p>}
    {!!page?.items.length && <div className="operation-table" role="region" aria-label="Cierres de esta sucursal" tabIndex={0}>
      <table><thead><tr><th scope="col">Operador</th><th scope="col">Cierre</th><th scope="col">Esperado</th><th scope="col">Contado</th><th scope="col">Diferencia</th><th scope="col">Arqueo</th></tr></thead>
        <tbody>{page.items.map(row => <tr key={row.cashSessionId}><td>{row.displayName}</td><td><DateText value={row.closedAt} /></td>
          <td>S/ {decimalLabel(row.expectedCashAmount, 2)}</td><td>S/ {decimalLabel(row.countedCashAmount, 2)}</td><td>S/ {decimalLabel(row.cashDifference, 2)}</td>
          <td><Button variant="quiet" disabled={blocked || history.isFetching} onClick={() => select(row.cashSessionId)}>Ver arqueo de {row.displayName}</Button></td></tr>)}</tbody>
      </table></div>}
  </DataSection>
}
