import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { ApiErrorState, isUuid, useRuntime, useSession } from '../access'
import { ApiError } from '../../shared/api/http'
import type { HttpClient } from '../../shared/api/http'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Panel } from '../../shared/ui/Panel'
import { Notice } from '../../shared/ui/Notice'
import { LoadingState } from '../../shared/ui/States'
import { PageHeader } from '../../shared/ui/PageHeader'
import { platformApi } from './api'
import type { LicenseAction } from './api'
import { usePlatformConsole } from './usePlatformConsole'
import '../../shared/styles/operations.css'

export function PlatformPage() {
  const { http } = useRuntime(); const session = useSession(); const client = useQueryClient(); const [params] = useSearchParams()
  const admission = useQuery({ queryKey: ['platform-admission', session.data?.userId], queryFn: ({ signal }) => platformApi(http!).admission(signal), enabled: Boolean(http && session.data), retry: false, refetchInterval: 60_000, refetchOnWindowFocus: true })
  useEffect(() => {
    if (admission.isError) { void client.cancelQueries({ queryKey: ['platform'] }); client.removeQueries({ queryKey: ['platform'] }) }
  }, [client, admission.isError])
  useEffect(() => () => { void client.cancelQueries({ queryKey: ['platform'] }); client.removeQueries({ queryKey: ['platform'] }); client.removeQueries({ queryKey: ['platform-admission'] }) }, [client])
  if (!http) return <Notice tone="error">Se requiere HTTPS y el backend real.</Notice>
  if (admission.isPending) return <LoadingState label="Verificando autoridad independiente de plataforma…" />
  if (admission.isError) return <><Notice tone="warning">La plataforma requiere activación protegida y aprobación independiente. Ser Owner no concede este acceso.</Notice><ApiErrorState error={admission.error} retry={() => void admission.refetch()} /></>
  if (!session.data) return null
  return <PlatformConsole key={`${session.data.userId}/${params.get('tenant') ?? ''}`} http={http} userId={session.data.userId} />
}

function PlatformConsole({ http, userId }: { http: HttpClient; userId: string }) {
  const p = usePlatformConsole(http, userId)
  const [name, setName] = useState(''); const [maxBranches, setMaxBranches] = useState(2); const [createConfirmed, setCreateConfirmed] = useState(false)
  const [ownerId, setOwnerId] = useState(''); const [ownerConfirmed, setOwnerConfirmed] = useState(false)
  const [action, setAction] = useState<LicenseAction>('renew'); const [expires, setExpires] = useState(''); const [status, setStatus] = useState<'trial' | 'active' | 'grace'>('active'); const [licenseConfirmed, setLicenseConfirmed] = useState(false)
  const [reconciled, setReconciled] = useState(false)
  const deniedQuery = [p.list.error, p.detail.error].some(error => error instanceof ApiError && [401, 403].includes(error.status))
  if (p.denied || deniedQuery) return <Notice tone="error">La autoridad de plataforma ya no está disponible. Vuelve a ingresar o solicita su revisión.</Notice>
  return <div className="operations"><PageHeader title="Administración de plataforma" description="Solo negocios, licencias y alta inicial. Sin acceso a ventas, precios, stock o reportes privados globales." />
    {p.error != null && <ApiErrorState error={p.error} />}{p.notice && <p role="status">{p.notice}</p>}
    <Panel><div className="operation-body"><h2>Negocios registrados</h2>{p.list.isFetching && <p role="status">Consultando negocios…</p>}{p.list.isError && <ApiErrorState error={p.list.error} retry={() => void p.list.refetch()} />}
      {p.list.isSuccess && !p.list.isFetching && <>{!p.list.data.items.length && <p>No hay negocios en esta página.</p>}<ul>{p.list.data.items.map(row => <li key={row.tenantId}><Button disabled={p.locked} onClick={() => { p.select(row.tenantId); setOwnerId(''); setOwnerConfirmed(false); setLicenseConfirmed(false) }}>{row.tradingName}</Button></li>)}</ul><Button disabled={p.locked} onClick={() => p.page(null)}>Primera página</Button><Button disabled={p.locked || !p.list.data.nextAfterTenantId} onClick={() => p.page(p.list.data?.nextAfterTenantId ?? null)}>Siguiente página</Button></>}
    </div></Panel>
    {p.creationKey && <Notice tone="warning">Alta pendiente de conciliación, también después de recargar. No crees otra botica ni cambies la clave. Un resultado todavía no visible no demuestra que falló.<Button disabled={p.busy || p.denied} onClick={() => void p.reconcileCreation()}>Consultar constancia original sin repetir POST</Button></Notice>}
    <Panel><form className="operation-body" onSubmit={e => { e.preventDefault(); if (!createConfirmed || p.locked || !name.trim()) return; setCreateConfirmed(false); const start = new Date(); void p.create({ tradingName: name.trim(), startsAt: start.toISOString(), expiresAt: new Date(start.getTime() + 30 * 24 * 60 * 60 * 1000).toISOString(), maxBranches, status: 'trial' }) }}><h2>Crear botica con prueba de 30 días</h2>
      <TextField label="Nombre comercial" value={name} required disabled={p.locked} onChange={e => { setName(e.target.value); setCreateConfirmed(false) }} />
      <TextField label="Máximo de sucursales (1–5)" type="number" min={1} max={5} step={1} value={maxBranches} disabled={p.locked} onChange={e => { setMaxBranches(Number(e.target.value)); setCreateConfirmed(false) }} />
      <p>Estado trial, inicio UTC al confirmar, vencimiento 30 días después. El backend conserva el resultado original por clave de idempotencia.</p>
      <label><input type="checkbox" disabled={p.locked} checked={createConfirmed} onChange={e => setCreateConfirmed(e.target.checked)} /> Confirmo crear esta botica y su licencia.</label><Button type="submit" disabled={p.locked || !createConfirmed || !name.trim() || !Number.isInteger(maxBranches) || maxBranches < 1 || maxBranches > 5}>Crear botica de prueba</Button>
    </form></Panel>
    {p.detail.isFetching && <p role="status">Consultando licencia y alta inicial…</p>}{p.detail.isError && <ApiErrorState error={p.detail.error} retry={() => void p.detail.refetch()} />}
    {p.data && <Panel><div className="operation-body"><h2>{p.data.tenant.tradingName} · licencia</h2><dl><dt>Estado</dt><dd>{p.data.license.status}</dd><dt>Inicio UTC</dt><dd>{p.data.license.startsAt}</dd><dt>Vencimiento UTC</dt><dd>{p.data.license.expiresAt}</dd><dt>Máximo de sucursales</dt><dd>{p.data.license.maxBranches}</dd></dl>
      <Button disabled={p.busy} onClick={() => { setReconciled(false); void p.refresh() }}>Reconsultar licencia y Owner</Button>
      {p.pending && !p.creationKey && <Notice tone="warning">Operación pendiente: {p.pending}. No repitas la escritura. Reconsulta y verifica el resultado original antes de continuar.<label><input type="checkbox" checked={reconciled} disabled={!p.reconciliationRead || p.busy} onChange={e => setReconciled(e.target.checked)} /> Verifiqué el resultado y que no hay solicitud en curso.</label><Button disabled={!reconciled || !p.reconciliationRead || p.busy} onClick={() => { p.acknowledge(); setReconciled(false) }}>Continuar tras conciliación</Button></Notice>}
      {p.data.initialOwnerUserId ? <><p>Existe historial de Owner; el bootstrap inicial permanece cerrado.</p><Button disabled={p.busy} onClick={() => void p.lookup(p.data!.initialOwnerUserId!)}>Verificar identidad del Owner inicial</Button>{p.identity?.userId === p.data.initialOwnerUserId && <p>Owner inicial registrado: {p.identity.displayName} · {p.identity.email}</p>}</> : <form onSubmit={e => { e.preventDefault(); if (ownerConfirmed && p.identity?.userId === ownerId && !p.locked) { setOwnerConfirmed(false); void p.bootstrap() } }}><h3>Primer Owner</h3><p>La cuenta destino debe haber ingresado con Google en este mismo entorno. El identificador se obtiene en su selección de boticas; no autoriza por sí mismo.</p>
        <TextField label="Identificador interno del futuro Owner" value={ownerId} hint="UUID de la sección Mi identidad de su cuenta Google en este entorno." disabled={p.locked} onChange={e => { setOwnerId(e.target.value.trim().toLowerCase()); setOwnerConfirmed(false); p.setIdentity(null) }} />
        <Button disabled={p.locked || !isUuid(ownerId)} onClick={() => void p.lookup(ownerId)}>Verificar identidad Google registrada</Button>
        {p.identity && <p>Identidad verificada por el servidor: {p.identity.displayName} · {p.identity.email}</p>}
        <label><input type="checkbox" disabled={p.locked || p.identity?.userId !== ownerId} checked={ownerConfirmed} onChange={e => setOwnerConfirmed(e.target.checked)} /> Confirmo que esta es la cuenta destinataria y autorizo el primer Owner.</label><Button type="submit" disabled={p.locked || !ownerConfirmed || p.identity?.userId !== ownerId}>Asignar primer Owner</Button>
      </form>}
      <form onSubmit={e => { e.preventDefault(); if (!licenseConfirmed || p.locked) return; setLicenseConfirmed(false); if (action === 'renew') void p.change(action, { expiresAt: expires }); else if (action === 'reactivate') void p.change(action, { status }); else void p.change(action, {}) }}><h3>Cambiar licencia</h3>
        <label>Operación<select aria-label="Operación de licencia" value={action} disabled={p.locked} onChange={e => { setAction(e.target.value as LicenseAction); setLicenseConfirmed(false) }}><option value="renew">Renovar</option><option value="suspend">Suspender</option><option value="reactivate">Reactivar</option></select></label>
        {action === 'renew' && <TextField label="Nuevo vencimiento UTC (ISO 8601 con Z)" value={expires} placeholder="AAAA-MM-DDTHH:mm:ssZ" required disabled={p.locked} onChange={e => { setExpires(e.target.value); setLicenseConfirmed(false) }} />}
        {action === 'reactivate' && <label>Estado al reactivar<select aria-label="Estado al reactivar" value={status} disabled={p.locked} onChange={e => { setStatus(e.target.value as typeof status); setLicenseConfirmed(false) }}><option value="trial">trial</option><option value="active">active</option><option value="grace">grace</option></select></label>}
        <label><input type="checkbox" disabled={p.locked} checked={licenseConfirmed} onChange={e => setLicenseConfirmed(e.target.checked)} /> Confirmo el cambio de la licencia seleccionada.</label><Button type="submit" disabled={p.locked || !licenseConfirmed}>Aplicar cambio de licencia</Button>
      </form>
    </div></Panel>}
  </div>
}
