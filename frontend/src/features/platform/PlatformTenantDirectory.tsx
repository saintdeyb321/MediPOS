import { useState } from 'react'
import { Button } from '../../shared/ui/Button'
import { Panel } from '../../shared/ui/Panel'
import { TextField } from '../../shared/ui/TextField'
import { Badge } from '../../shared/ui/Badge'
import { DateText } from '../../shared/ui/DateText'
import { EmptyState } from '../../shared/ui/States'
import { licenseLabel } from '../../shared/ui/licenseLabel'
import { PlatformReadError } from './PlatformAdmissionError'
import type { usePlatformConsole } from './usePlatformConsole'
import { expiresSoon, ownerLabel } from './presentation'

export type PlatformConsoleState = ReturnType<typeof usePlatformConsole>

export function PlatformTenantDirectory({ p, area, select }: { p: PlatformConsoleState; area: string; select: (id: string) => void }) {
  const [filter, setFilter] = useState('')
  const [status, setStatus] = useState('')
  const [soon, setSoon] = useState(false)
  const owners = area === 'owners'
  const licenses = area === 'licenses'
  const rows = p.list.data?.items.filter(row => {
    const admin = row.administration
    if (owners && admin?.ownerStatus !== 'pending') return false
    if (status && admin?.licenseStatus !== status) return false
    if (soon && (!admin || !expiresSoon(admin.expiresAt, p.list.dataUpdatedAt))) return false
    return [row.tradingName, owners ? admin?.ownerEmail ?? '' : ''].some(value => value.toLocaleLowerCase('es-PE').includes(filter.trim().toLocaleLowerCase('es-PE')))
  })
  return <Panel><div className="operation-body">
    <div className="platform-context"><p className="platform-note">Filtros sobre esta página, hasta 20 boticas.{(p.list.isError || !p.admitted) && p.list.data && ' Datos de la última lectura.'}</p>
      {!p.list.isError && p.admitted && <Button variant="secondary" disabled={p.busy || p.list.isFetching} onClick={() => void p.list.refetch()}>Actualizar boticas</Button>}</div>
    <div className="platform-filters">
      <TextField label="Filtrar esta página" placeholder={owners ? 'Botica o correo' : 'Nombre de la botica'} value={filter} onChange={event => setFilter(event.target.value)} />
      {licenses && <><label>Estado de licencia<select value={status} onChange={event => setStatus(event.target.value)}><option value="">Todos</option>
        {['trial', 'active', 'grace', 'suspended', 'cancelled', 'purge_pending', 'purged'].map(value => <option key={value} value={value}>{licenseLabel(value)}</option>)}</select></label>
      <label><input type="checkbox" checked={soon} onChange={event => setSoon(event.target.checked)} /> Vence en 7 días</label></>}
    </div>
    {p.list.isFetching && <p role="status">{p.list.data ? 'Actualizando boticas…' : 'Consultando negocios…'}</p>}
    <PlatformReadError error={p.list.error} failedAt={p.list.errorUpdatedAt} fetching={p.list.isFetching} retry={p.admitted ? () => void p.list.refetch() : undefined} />
    {p.list.data && <>
      {!rows?.length ? <EmptyState title={owners ? 'Sin propietarios pendientes en esta página' : 'Sin boticas para esta vista'} description={owners ? 'Consulta las demás páginas para revisar otras asignaciones.' : 'Ajusta los filtros o vuelve a la primera página.'} /> :
        <div className="catalog-table"><table><thead><tr><th>Botica</th>
          {!licenses && <th>{owners ? 'Gmail designado' : 'Propietario'}</th>}
          {owners ? <th>Fecha de designación</th> : <th>Licencia</th>}<th>Acciones</th></tr></thead><tbody>{rows.map(row => {
            const admin = row.administration
            return <tr key={row.tenantId}><th scope="row">{row.tradingName}</th>
              {!licenses && <td>{admin ? <><Badge tone={admin.ownerStatus === 'pending' ? 'warning' : 'neutral'}>{ownerLabel(admin.ownerStatus)}</Badge><br />{admin.ownerName ?? admin.ownerEmail ?? 'Sin destinatario registrado'}</> : 'Sin información administrativa'}</td>}
              {owners ? <td>{admin?.ownerAssignedAt ? <DateText value={admin.ownerAssignedAt} /> : 'Sin fecha disponible'}</td> :
                <td>{admin ? <><Badge tone={admin.licenseStatus === 'suspended' ? 'warning' : 'info'}>{licenseLabel(admin.licenseStatus)}</Badge>
                  <div>{licenses && admin.startsAt && <>Inicio <DateText value={admin.startsAt} /> · </>}Vence <DateText value={admin.expiresAt} /></div>
                  <small>Hasta {admin.maxBranches} sucursales</small></> : 'Licencia no disponible'}</td>}
              <td><Button variant="secondary" disabled={p.locked || p.list.isError} onClick={() => select(row.tenantId)}>Gestionar {row.tradingName}</Button></td>
            </tr>
          })}</tbody></table></div>}
      <div className="operation-actions"><Button variant="secondary" disabled={!p.admitted || p.busy || !p.after} onClick={() => p.page(null)}>Primera página</Button><Button disabled={!p.admitted || p.busy || !p.list.data.nextAfterTenantId} onClick={() => p.page(p.list.data?.nextAfterTenantId ?? null)}>Siguiente página</Button></div>
    </>}
  </div></Panel>
}
