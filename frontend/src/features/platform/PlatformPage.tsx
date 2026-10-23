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
import { Modal } from '../../shared/ui/Modal'
import { DateText } from '../../shared/ui/DateText'
import { licenseLabel } from '../../shared/ui/licenseLabel'
import { InlineHelp } from '../../shared/ui/FormLayout'
import { UtcDateTimeField } from '../../shared/ui/UtcDateTimeField'
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
  const [categoryName, setCategoryName] = useState(''); const [categoryConfirmed, setCategoryConfirmed] = useState(false); const [categoryReconciled, setCategoryReconciled] = useState(false)
  const [section, setSection] = useState<'businesses' | 'license' | 'owner' | 'categories' | 'status'>(() => p.categoryPending ? 'categories' : p.tenantId ? 'license' : 'businesses')
  const [task, setTask] = useState<'create' | 'category' | 'owner' | 'license' | null>(null)
  const [taskHidden, setTaskHidden] = useState(false)
  function openTask(value: NonNullable<typeof task>) { setTask(value); setTaskHidden(false) }
  const categoryExists = p.categories.data?.some(row => row.name.trim().toLocaleLowerCase('es-PE') === categoryName.trim().toLocaleLowerCase('es-PE'))
  const canCreateCategory = !p.locked && p.categoriesRead && p.categories.data && p.categories.data.length < 100 && !categoryExists && categoryName.trim().length > 0 && categoryName.trim().length <= 128
  const categoryCandidates = p.categoriesRead ? p.categories.data?.filter(row => row.name.trim().toLocaleLowerCase('es-PE') === p.categoryPending?.trim().toLocaleLowerCase('es-PE')) ?? [] : []
  const deniedQuery = [p.list.error, p.detail.error].some(error => error instanceof ApiError && [401, 403].includes(error.status))
  if (p.denied || deniedQuery) return <Notice tone="error">La autoridad de plataforma ya no está disponible. Vuelve a ingresar o solicita su revisión.</Notice>
  const creationRecovery = p.creationKey && <Notice tone="warning">Alta pendiente de conciliación. No crees otra botica ni cambies la operación original. Una lectura todavía vacía no demuestra rollback.<Button disabled={p.busy || p.denied} onClick={() => void p.reconcileCreation()}>Consultar constancia original sin repetir la creación</Button></Notice>
  const operationRecovery = p.pending && !p.creationKey && <Notice tone="warning">Operación pendiente. Reconsulta y verifica el resultado original antes de continuar.<Button disabled={p.busy} onClick={() => { setReconciled(false); void p.refresh() }}>Reconsultar licencia y propietario</Button><label><input type="checkbox" checked={reconciled} disabled={!p.reconciliationRead || p.busy} onChange={e => setReconciled(e.target.checked)} /> Verifiqué el resultado y que no hay solicitud en curso.</label><Button disabled={!reconciled || !p.reconciliationRead || p.busy} onClick={() => { p.acknowledge(); setReconciled(false) }}>Continuar tras conciliación</Button></Notice>
  const categoryRecovery = p.categoryPending && <Notice tone="warning">Alta de categoría pendiente: {p.categoryPending}. No repitas el envío; una lista vacía no demuestra que falló.
    {categoryCandidates.length > 1 && <p>Hay nombres coincidentes. La conciliación sigue bloqueada.</p>}
    <Button disabled={p.busy} onClick={() => { setCategoryReconciled(false); void p.readCategories() }}>Reconsultar categorías</Button>
    {categoryCandidates.map(row => <p key={row.id}>{row.name} · <DateText value={row.createdAt} /></p>)}
    <label><input type="checkbox" disabled={p.busy || categoryCandidates.length !== 1} checked={categoryReconciled} onChange={e => setCategoryReconciled(e.target.checked)} /> Verifiqué el registro y la finalización de la petición original.</label><Button disabled={p.busy || !categoryReconciled || categoryCandidates.length !== 1} onClick={() => { p.acknowledgeCategory(); setCategoryReconciled(false) }}>Continuar tras conciliar categoría</Button>
  </Notice>
  return <div className="operations"><PageHeader title="Administración de plataforma" description="Gestiona negocios, licencias y altas iniciales. Esta autoridad es independiente de cualquier rol en una botica." action={<Button disabled={p.locked} onClick={() => openTask('create')}>Nueva botica</Button>} />
    <nav className="task-tabs" aria-label="Administración de plataforma">{([['businesses', 'Negocios'], ['license', 'Licencias'], ['owner', 'Asignar propietario'], ['categories', 'Categorías globales'], ['status', 'Estado de plataforma']] as const).map(([id, label]) => <Button key={id} variant="quiet" aria-pressed={section === id} disabled={p.busy} onClick={() => setSection(id)}>{label}</Button>)}</nav>
    {p.error != null && <ApiErrorState error={p.error} />}{p.notice && <p role="status">{p.notice}</p>}
    {task && taskHidden && (p.pending || p.creationKey || p.categoryPending) && <Button variant="secondary" disabled={p.busy} onClick={() => setTaskHidden(false)}>Revisar solicitud pendiente sin reenviar</Button>}
    <Panel hidden={section !== 'businesses'}><div className="operation-body"><h2>Negocios registrados</h2>{p.list.isFetching && <p role="status">Consultando negocios…</p>}{p.list.isError && <ApiErrorState error={p.list.error} retry={() => void p.list.refetch()} />}
      {p.list.isSuccess && !p.list.isFetching && <>{!p.list.data.items.length && <p>No hay negocios en esta página.</p>}<ul>{p.list.data.items.map(row => <li key={row.tenantId}><Button disabled={p.locked} onClick={() => { p.select(row.tenantId); setOwnerId(''); setOwnerConfirmed(false); setLicenseConfirmed(false) }}>{row.tradingName}</Button></li>)}</ul><Button disabled={p.locked} onClick={() => p.page(null)}>Primera página</Button><Button disabled={p.locked || !p.list.data.nextAfterTenantId} onClick={() => p.page(p.list.data?.nextAfterTenantId ?? null)}>Siguiente página</Button></>}
    </div></Panel>
    {p.creationKey && <Notice tone="warning">Alta pendiente de conciliación, también después de recargar. No crees otra botica ni cambies la clave. Un resultado todavía no visible no demuestra que falló.<Button disabled={p.busy || p.denied} onClick={() => void p.reconcileCreation()}>Consultar constancia original sin repetir POST</Button></Notice>}
    <Modal open={task === 'create' && !taskHidden} title="Crear botica con prueba gratuita" onClose={() => { setTask(null); setName(''); setMaxBranches(2); setCreateConfirmed(false) }} onRecoveryClose={() => setTaskHidden(true)} dirty={Boolean(name || createConfirmed || maxBranches !== 2)} busy={p.busy} recovery={Boolean(p.creationKey)}>
    {p.error != null && <ApiErrorState error={p.error} />}{creationRecovery}
    <form className="operation-body" onSubmit={e => { e.preventDefault(); if (!createConfirmed || p.locked || !name.trim()) return; setCreateConfirmed(false); const start = new Date(); void p.create({ tradingName: name.trim(), startsAt: start.toISOString(), expiresAt: new Date(start.getTime() + 30 * 24 * 60 * 60 * 1000).toISOString(), maxBranches, status: 'trial' }) }}><h2>Prueba de 30 días</h2>
      <TextField label="Nombre comercial" value={name} required disabled={p.locked} onChange={e => { setName(e.target.value); setCreateConfirmed(false) }} />
      <TextField label="Máximo de sucursales (1–5)" type="number" min={1} max={5} step={1} value={maxBranches} disabled={p.locked} onChange={e => { setMaxBranches(Number(e.target.value)); setCreateConfirmed(false) }} />
      <p>La prueba comienza al confirmar y vence 30 días después. Si la respuesta se interrumpe, consulta el resultado original antes de intentar otra alta.</p>
      <label><input type="checkbox" disabled={p.locked} checked={createConfirmed} onChange={e => setCreateConfirmed(e.target.checked)} /> Confirmo crear esta botica y su licencia.</label><Button type="submit" disabled={p.locked || !createConfirmed || !name.trim() || !Number.isInteger(maxBranches) || maxBranches < 1 || maxBranches > 5}>Crear botica de prueba</Button>
    </form></Modal>
    <Panel hidden={section !== 'categories' && !p.categoryPending}><div className="operation-body"><div className="operation-actions"><h2>Categorías globales</h2><Button disabled={p.locked || !p.categoriesRead || !p.categories.data || p.categories.data.length >= 100} onClick={() => openTask('category')}>Nueva categoría</Button></div><p>Referencias compartidas; no crean productos ni existencias. Consulta antes de añadir otra.</p>
      <Button disabled={p.busy} onClick={() => { setCategoryConfirmed(false); setCategoryReconciled(false); void p.readCategories() }}>Consultar categorías globales</Button>
      {p.categoriesRead && p.categories.data && <><ul>{p.categories.data.map(row => <li key={row.id}>{row.name} · creada {row.createdAt}</li>)}</ul>{!p.categories.data.length && <p>No hay categorías activas.</p>}{p.categories.data.length >= 100 && <Notice tone="warning">La lectura está acotada a 100 categorías. No crees otra sin revisar el catálogo completo.</Notice>}</>}
      {p.categoryPending && <Notice tone="warning">Alta de categoría pendiente: {p.categoryPending}. No repitas el POST. Reconsulta y verifica el resultado original; una lista vacía no demuestra que falló.
        {categoryCandidates.length > 1 && <p>Hay categorías con el mismo nombre. La conciliación permanece bloqueada; revisa los registros, sin repetir escrituras.</p>}
        <label><input type="checkbox" disabled={p.busy || categoryCandidates.length !== 1} checked={categoryReconciled} onChange={e => setCategoryReconciled(e.target.checked)} /> Verifiqué la categoría registrada y que la petición original terminó.</label><Button disabled={p.busy || !categoryReconciled || categoryCandidates.length !== 1} onClick={() => { p.acknowledgeCategory(); setCategoryReconciled(false) }}>Continuar tras conciliar categoría</Button></Notice>}
      <Modal open={task === 'category' && !taskHidden} title="Crear categoría global" onClose={() => { setTask(null); setCategoryName(''); setCategoryConfirmed(false) }} onRecoveryClose={() => setTaskHidden(true)} dirty={Boolean(categoryName || categoryConfirmed)} busy={p.busy} recovery={Boolean(p.categoryPending)}>
      {p.error != null && <ApiErrorState error={p.error} />}{categoryRecovery}{p.notice && <p role="status">{p.notice}</p>}
      <form className="operation-body" onSubmit={e => { e.preventDefault(); if (categoryConfirmed && canCreateCategory) { setCategoryConfirmed(false); void p.createCategory(categoryName) } }}>
        <TextField label="Nombre de la categoría global" value={categoryName} maxLength={128} disabled={p.locked} onChange={e => { setCategoryName(e.target.value); setCategoryConfirmed(false) }} />
        {p.categoriesRead && categoryExists && <p>Esa categoría ya figura en la lectura. Reutilízala; no se creará otra.</p>}
        <label><input type="checkbox" disabled={!canCreateCategory} checked={categoryConfirmed} onChange={e => setCategoryConfirmed(e.target.checked)} /> Confirmo crear esta referencia global, sin productos.</label><Button type="submit" disabled={!canCreateCategory || !categoryConfirmed}>Crear categoría global una vez</Button>
      </form>
      </Modal>
    </div></Panel>
    {p.detail.isFetching && <p role="status">Consultando licencia y alta inicial…</p>}{p.detail.isError && <ApiErrorState error={p.detail.error} retry={() => void p.detail.refetch()} />}
    {p.data && <Panel hidden={section !== 'license' && section !== 'owner' && !p.pending}><div className="operation-body"><h2>{p.data.tenant.tradingName}</h2><dl className="summary-grid"><div><dt>Licencia</dt><dd>{licenseLabel(p.data.license.status)}</dd></div><div><dt>Inicio</dt><dd><DateText value={p.data.license.startsAt} /></dd></div><div><dt>Vencimiento</dt><dd><DateText value={p.data.license.expiresAt} /></dd></div><div><dt>Máximo de sucursales</dt><dd>{p.data.license.maxBranches}</dd></div></dl>
      <div className="operation-actions"><Button variant="secondary" disabled={p.locked} onClick={() => openTask('license')}>Gestionar licencia</Button>{!p.data.initialOwnerUserId && <Button variant="secondary" disabled={p.locked} onClick={() => openTask('owner')}>Asignar primer propietario</Button>}</div>
      <Button disabled={p.busy} onClick={() => { setReconciled(false); void p.refresh() }}>Reconsultar licencia y Owner</Button>
      {p.pending && !p.creationKey && <Notice tone="warning">Operación pendiente: {p.pending}. No repitas la escritura. Reconsulta y verifica el resultado original antes de continuar.<label><input type="checkbox" checked={reconciled} disabled={!p.reconciliationRead || p.busy} onChange={e => setReconciled(e.target.checked)} /> Verifiqué el resultado y que no hay solicitud en curso.</label><Button disabled={!reconciled || !p.reconciliationRead || p.busy} onClick={() => { p.acknowledge(); setReconciled(false) }}>Continuar tras conciliación</Button></Notice>}
      {p.data.initialOwnerUserId ? <><p>Ya existe un propietario inicial registrado; el alta inicial permanece cerrada.</p><Button variant="secondary" disabled={p.locked} onClick={() => void p.lookup(p.data!.initialOwnerUserId!)}>Verificar identidad del propietario inicial</Button>{p.identity?.userId === p.data.initialOwnerUserId && <p>Propietario inicial: {p.identity.displayName} · {p.identity.email}</p>}</> : <Modal open={task === 'owner' && !taskHidden} title="Asignar primer propietario" onClose={() => { setTask(null); setOwnerId(''); setOwnerConfirmed(false); p.setIdentity(null) }} onRecoveryClose={() => setTaskHidden(true)} dirty={Boolean(ownerId || ownerConfirmed)} busy={p.busy} recovery={Boolean(p.pending)}>
      {p.error != null && <ApiErrorState error={p.error} />}{operationRecovery}
      <form className="operation-body" onSubmit={e => { e.preventDefault(); if (ownerConfirmed && p.identity?.userId === ownerId && !p.locked) { setOwnerConfirmed(false); void p.bootstrap() } }}><h3>Verificar cuenta Google</h3><p>La cuenta destinataria debe haber ingresado en este mismo entorno. Copia su identificador desde «Mi identidad» y verifica la identidad devuelta antes de asignar permisos.</p>
        <TextField label="Identificador interno del futuro Owner" value={ownerId} hint="UUID de la sección Mi identidad de su cuenta Google en este entorno." disabled={p.locked} onChange={e => { setOwnerId(e.target.value.trim().toLowerCase()); setOwnerConfirmed(false); p.setIdentity(null) }} />
        <Button disabled={p.locked || !isUuid(ownerId)} onClick={() => void p.lookup(ownerId)}>Verificar identidad Google registrada</Button>
        {p.identity && <p>Identidad verificada por el servidor: {p.identity.displayName} · {p.identity.email}</p>}
        <label><input type="checkbox" disabled={p.locked || p.identity?.userId !== ownerId} checked={ownerConfirmed} onChange={e => setOwnerConfirmed(e.target.checked)} /> Confirmo que esta es la cuenta destinataria y autorizo asignarla como primer propietario.</label><Button type="submit" disabled={p.locked || !ownerConfirmed || p.identity?.userId !== ownerId}>Asignar primer propietario</Button>
      </form><InlineHelp title="Por qué se solicita un identificador">El contrato actual verifica una cuenta Google ya registrada por su identificador interno. Todavía no existe una invitación de propietario por correo; esta pantalla no la simula.</InlineHelp></Modal>}
      <Modal open={task === 'license' && !taskHidden} title="Gestionar licencia" onClose={() => { setTask(null); setLicenseConfirmed(false); setExpires('') }} onRecoveryClose={() => setTaskHidden(true)} dirty={Boolean(expires || licenseConfirmed)} busy={p.busy} recovery={Boolean(p.pending)}>
      {p.error != null && <ApiErrorState error={p.error} />}{operationRecovery}{p.notice && <p role="status">{p.notice}</p>}
      <form className="operation-body" onSubmit={e => { e.preventDefault(); if (!licenseConfirmed || p.locked) return; setLicenseConfirmed(false); if (action === 'renew') void p.change(action, { expiresAt: expires }); else if (action === 'reactivate') void p.change(action, { status }); else void p.change(action, {}) }}><h3>{p.data.tenant.tradingName}</h3>
        <label>Operación<select aria-label="Operación de licencia" value={action} disabled={p.locked} onChange={e => { setAction(e.target.value as LicenseAction); setLicenseConfirmed(false) }}><option value="renew">Renovar</option><option value="suspend">Suspender</option><option value="reactivate">Reactivar</option></select></label>
        {action === 'renew' && <UtcDateTimeField label="Nuevo vencimiento" value={expires} required disabled={p.locked} change={value => { setExpires(value); setLicenseConfirmed(false) }} />}
        {action === 'reactivate' && <label>Estado al reactivar<select aria-label="Estado al reactivar" value={status} disabled={p.locked} onChange={e => { setStatus(e.target.value as typeof status); setLicenseConfirmed(false) }}><option value="trial">Prueba gratuita</option><option value="active">Activa</option><option value="grace">Periodo de gracia</option></select></label>}
        <label><input type="checkbox" disabled={p.locked} checked={licenseConfirmed} onChange={e => setLicenseConfirmed(e.target.checked)} /> Confirmo el cambio de la licencia seleccionada.</label><Button type="submit" disabled={p.locked || !licenseConfirmed}>Aplicar cambio de licencia</Button>
      </form></Modal>
    </div></Panel>}
    {(section === 'license' || section === 'owner') && !p.tenantId && <Notice>Selecciona primero un negocio desde la sección Negocios.</Notice>}
    {section === 'status' && <Panel><div className="operation-body"><h2>Sesión de plataforma</h2><p>Acceso admitido por el servidor para esta cuenta. La disponibilidad se vuelve a comprobar durante la sesión.</p><p>Esta autoridad no concede acceso global a ventas, precios, stock o reportes privados. La licencia comercial de una botica y la autoridad administrativa son independientes.</p><InlineHelp title="Detalles operativos">La interfaz no habilita ni renueva ventanas administrativas. Su activación y cierre dependen del procedimiento protegido del entorno.</InlineHelp></div></Panel>}
  </div>
}
