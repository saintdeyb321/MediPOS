import { useState } from 'react'
import { ApiErrorState } from '../access'
import { Button } from '../../shared/ui/Button'
import { Panel } from '../../shared/ui/Panel'
import { TextField } from '../../shared/ui/TextField'
import { Badge } from '../../shared/ui/Badge'
import { DateText } from '../../shared/ui/DateText'
import { licenseLabel } from '../../shared/ui/licenseLabel'
import type { usePlatformConsole } from './usePlatformConsole'
import { ownerLabel } from './presentation'

export type PlatformConsoleState = ReturnType<typeof usePlatformConsole>

export function PlatformTenantDirectory({ p, area, select }: { p: PlatformConsoleState; area: string; select: (id: string) => void }) {
  const [filter, setFilter] = useState('')
  const rows = p.list.data?.items.filter(row => (area !== 'owners' || row.administration?.ownerStatus === 'pending') && row.tradingName.toLocaleLowerCase('es-PE').includes(filter.trim().toLocaleLowerCase('es-PE')))
  return <Panel><div className="operation-body">
    <div className="platform-context"><h2>{area === 'owners' ? 'Propietarios pendientes' : area === 'licenses' ? 'Licencias de las boticas' : 'Boticas registradas'}</h2><Button variant="secondary" disabled={p.busy || p.list.isFetching} onClick={() => void p.list.refetch()}>Actualizar boticas</Button></div>
    <p>Consulta de la página actual, hasta 20 boticas. El filtro no busca en otras páginas.</p>
    <TextField label="Filtrar esta página" value={filter} onChange={event => setFilter(event.target.value)} />
    {p.list.isFetching && <p role="status">Consultando negocios…</p>}
    {p.list.isError && <ApiErrorState error={p.list.error} retry={() => void p.list.refetch()} />}
    {p.list.isSuccess && !p.list.isFetching && <>
      {!rows?.length ? <p>No hay boticas para esta vista. {area === 'owners' && 'Revisa las otras páginas antes de concluir que no hay pendientes.'}</p> :
        <div className="catalog-table"><table><thead><tr><th>Botica</th><th>Propietario</th><th>Licencia</th><th>Acciones</th></tr></thead><tbody>{rows.map(row => <tr key={row.tenantId}>
          <th scope="row">{row.tradingName}</th><td><Badge tone={row.administration?.ownerStatus === 'pending' ? 'warning' : 'neutral'}>{ownerLabel(row.administration?.ownerStatus ?? 'unassigned')}</Badge><br />{row.administration?.ownerName ?? row.administration?.ownerEmail ?? 'Sin destinatario registrado'}</td>
          <td>{row.administration ? <>{licenseLabel(row.administration.licenseStatus)}<br /><DateText value={row.administration.expiresAt} /> · hasta {row.administration.maxBranches} sucursales</> : 'Consultar licencia'}</td>
          <td><Button variant="secondary" disabled={p.locked} onClick={() => select(row.tenantId)}>Gestionar {row.tradingName}</Button></td>
        </tr>)}</tbody></table></div>}
      <div className="operation-actions"><Button variant="secondary" disabled={p.locked} onClick={() => p.page(null)}>Primera página</Button><Button disabled={p.locked || !p.list.data.nextAfterTenantId} onClick={() => p.page(p.list.data?.nextAfterTenantId ?? null)}>Siguiente página</Button></div>
    </>}
  </div></Panel>
}
