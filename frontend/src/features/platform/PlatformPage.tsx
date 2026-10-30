import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { ShieldCheck } from 'lucide-react'
import { ApiErrorState, useRuntime, useSession } from '../access'
import { ApiError } from '../../shared/api/http'
import type { HttpClient } from '../../shared/api/http'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Panel } from '../../shared/ui/Panel'
import { Notice } from '../../shared/ui/Notice'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Modal } from '../../shared/ui/Modal'
import { DateText } from '../../shared/ui/DateText'
import { UtcDateTimeField } from '../../shared/ui/UtcDateTimeField'
import type { LicenseAction, TrialInput } from './api'
import { usePlatformConsole } from './usePlatformConsole'
import { NewTenantForm } from './NewTenantDialog'
import { PlatformTenantDirectory } from './PlatformTenantDirectory'
import { PlatformTenantDetails } from './PlatformTenantDetails'
import { PlatformRecovery } from './PlatformRecovery'
import { licenseActions } from './presentation'
import '../../shared/styles/operations.css'

// Admission belongs to PlatformLayout. Tenant selection must not remount a write's hook.
export function PlatformPage() {
  const { http } = useRuntime(); const session = useSession()
  return http && session.data ? <PlatformConsole key={session.data.userId} http={http} userId={session.data.userId} /> : null
}
function PlatformConsole({ http, userId }: { http: HttpClient; userId: string }) {
  const p = usePlatformConsole(http, userId); const [params, setParams] = useSearchParams()
  const area = params.get('area') ?? 'home'
  const titles: Record<string, string> = { home: 'Inicio de plataforma', businesses: 'Boticas', owners: 'Propietarios pendientes', licenses: 'Licencias', catalog: 'Catálogo global', account: 'Mi cuenta' }
  const [createDirty, setCreateDirty] = useState(false); const [createEpoch, setCreateEpoch] = useState(0)
  const [ownerEmail, setOwnerEmail] = useState(''); const [ownerConfirmed, setOwnerConfirmed] = useState(false)
  const [action, setAction] = useState<LicenseAction>('renew'); const [expires, setExpires] = useState(''); const [status, setStatus] = useState<'trial' | 'active' | 'grace'>('active'); const [licenseConfirmed, setLicenseConfirmed] = useState(false)
  const [categoryName, setCategoryName] = useState(''); const [categoryConfirmed, setCategoryConfirmed] = useState(false)
  const [task, setTask] = useState<'create' | 'category' | 'owner' | 'license' | null>(null)
  const [taskHidden, setTaskHidden] = useState(false); const [detailOpen, setDetailOpen] = useState(Boolean(p.tenantId))
  const denied = p.denied || [p.list.error, p.detail.error, p.initialOwner.error].some(error => error instanceof ApiError && [401, 403].includes(error.status))
  function openTask(next: NonNullable<typeof task>) { setTask(next); setTaskHidden(false); setDetailOpen(false) }
  function created() { setTask(null); setTaskHidden(false); setCreateDirty(false); setCreateEpoch(value => value + 1); setDetailOpen(true) }
  function operationRecovered() { setTask(null); setTaskHidden(false); setOwnerEmail(''); setOwnerConfirmed(false); setLicenseConfirmed(false); setExpires(''); setDetailOpen(true) }
  function categoryRecovered() { setTask(null); setTaskHidden(false); setCategoryName(''); setCategoryConfirmed(false) }
  async function create(input: TrialInput) { const saved = await p.create(input); if (saved) created(); return saved }
  function manage(next: 'owner' | 'license') {
    if (next === 'owner') { setOwnerEmail(p.initialOwner.data?.email ?? ''); setOwnerConfirmed(false) }
    setLicenseConfirmed(false); openTask(next)
  }
  function select(id: string) { p.select(id); setDetailOpen(true) }
  function setArea(next: string) { const copy = new URLSearchParams(params); copy.set('area', next); setParams(copy, { replace: true }) }
  const canCreateCategory = !p.locked && p.categoriesRead && p.categories.data && p.categories.data.length < 100 && categoryName.trim().length > 0 && categoryName.trim().length <= 128 && !p.categories.data.some(row => row.name.trim().toLocaleLowerCase('es-PE') === categoryName.trim().toLocaleLowerCase('es-PE'))
  if (denied) return <Notice tone="error">La autoridad de plataforma ya no está disponible. Vuelve a ingresar o solicita su revisión.</Notice>
  return <div className="operations">
    <PageHeader eyebrow="MEDIPOS · PLATAFORMA" title={titles[area] ?? 'Plataforma'} description="Administración de negocios y accesos; independiente de la operación comercial." icon={ShieldCheck} action={<Button disabled={p.locked} onClick={() => openTask('create')}>Nueva botica</Button>} />
    {p.notice && <p role="status" className="notice info">{p.notice}</p>}{p.error != null && !task && <ApiErrorState error={p.error} />}
    <PlatformRecovery p={p} kind="creation" recovered={created} /><PlatformRecovery p={p} kind="operation" recovered={operationRecovered} /><PlatformRecovery p={p} kind="category" recovered={categoryRecovered} />
    {task && taskHidden && (p.pending || p.creationKey || p.categoryPending) && <Button data-recovery-focus variant="secondary" disabled={p.busy} onClick={() => setTaskHidden(false)}>Revisar solicitud pendiente sin reenviar</Button>}
    {area === 'home' && <>
      {p.list.isFetching && <p role="status">Consultando indicadores de esta página…</p>}{p.list.isError && <ApiErrorState error={p.list.error} retry={() => void p.list.refetch()} />}
      {p.list.isSuccess && <><p>Indicadores de esta página; no son totales globales.</p><dl className="platform-kpis">
        <div><dt>Boticas en esta página</dt><dd>{p.list.data.items.length}</dd></div>
        <div><dt>Owners pendientes en esta página</dt><dd>{p.list.data.items.filter(row => row.administration?.ownerStatus === 'pending').length}</dd></div>
        <div><dt>Licencias por vencer en 7 días en esta página</dt><dd>{p.list.data.items.filter(row => row.administration && Date.parse(row.administration.expiresAt) >= p.list.dataUpdatedAt && Date.parse(row.administration.expiresAt) <= p.list.dataUpdatedAt + 7 * 86400000).length}</dd></div>
        <div><dt>Solicitudes por conciliar en esta vista</dt><dd>{Number(Boolean(p.pending || p.creationKey)) + Number(Boolean(p.categoryPending))}</dd></div>
      </dl></>}
      <Panel><div className="operation-body"><h2>Incorporar una nueva botica</h2><p>Designa el Gmail del Owner y la licencia. Su primer ingreso con Google verificado completará la asignación; no necesita un registro previo.</p><div className="operation-actions"><Button disabled={p.locked} onClick={() => openTask('create')}>Iniciar alta por Gmail</Button><Button variant="secondary" onClick={() => setArea('businesses')}>Ver boticas</Button></div></div></Panel>
    </>}
    {['businesses', 'owners', 'licenses'].includes(area) && <PlatformTenantDirectory p={p} area={area} select={select} />}
    {area === 'catalog' && <Panel><div className="operation-body"><div className="platform-context"><h2>Categorías globales</h2><Button disabled={p.locked || !p.categoriesRead || !p.categories.data || p.categories.data.length >= 100} onClick={() => openTask('category')}>Nueva categoría</Button></div><p>Referencias compartidas. No crean productos, precios ni existencias.</p>
      <Button variant="secondary" disabled={p.busy} onClick={() => { setCategoryConfirmed(false); void p.readCategories() }}>Consultar categorías globales</Button>
      {p.categoriesRead && p.categories.data && <>{!p.categories.data.length ? <p>No hay categorías activas.</p> : <ul>{p.categories.data.map(row => <li key={row.id}>{row.name} · <DateText value={row.createdAt} /></li>)}</ul>}{p.categories.data.length >= 100 && <Notice tone="warning">La lectura está acotada a 100 categorías. No crees otra sin revisar el catálogo completo.</Notice>}</>}
    </div></Panel>}
    {area === 'account' && <Panel><div className="operation-body"><h2>Cuenta administrativa</h2><p>Sesión Google con autoridad admitida por el servidor. No concede membresía comercial, acceso global a stock, ventas ni información privada de otras boticas.</p><p>La revocación depende de la configuración administrativa protegida y un reinicio controlado. No se modifica desde este panel.</p><div className="platform-account-actions"><Link to="/seleccionar-botica">Cambiar explícitamente a mis boticas</Link></div><p>El cierre de sesión está disponible en el menú de plataforma.</p></div></Panel>}
    {!titles[area] && <Notice>Selecciona un área desde el menú de plataforma.</Notice>}
    <PlatformTenantDetails p={p} open={detailOpen} close={() => setDetailOpen(false)} manage={manage} />
    <Modal open={task === 'create' && !taskHidden} wide title="Nueva botica" onClose={() => { setTask(null); setCreateDirty(false); setCreateEpoch(value => value + 1) }} onRecoveryClose={() => setTaskHidden(true)} dirty={createDirty} busy={p.busy} recovery={Boolean(p.creationKey)}>
      {p.error != null && <ApiErrorState error={p.error} />}<PlatformRecovery p={p} kind="creation" recovered={created} />
      <NewTenantForm key={createEpoch} locked={p.locked} create={create} dirty={setCreateDirty} />
    </Modal>
    <Modal open={task === 'category' && !taskHidden} title="Crear categoría global" onClose={() => { setTask(null); setCategoryName(''); setCategoryConfirmed(false) }} onRecoveryClose={() => setTaskHidden(true)} dirty={Boolean(categoryName || categoryConfirmed)} busy={p.busy} recovery={Boolean(p.categoryPending)}>
      {p.error != null && <ApiErrorState error={p.error} />}<PlatformRecovery p={p} kind="category" recovered={categoryRecovered} />
      <form className="operation-body" onSubmit={event => { event.preventDefault(); if (categoryConfirmed && canCreateCategory) { setCategoryConfirmed(false); void p.createCategory(categoryName.trim()).then(saved => { if (saved) { setTask(null); setCategoryName(''); setCategoryConfirmed(false) } }) } }}>
        <TextField label="Nombre de la categoría global" value={categoryName} maxLength={128} disabled={p.locked} onChange={event => { setCategoryName(event.target.value); setCategoryConfirmed(false) }} />
        {p.categories.data?.some(row => row.name.trim().toLocaleLowerCase('es-PE') === categoryName.trim().toLocaleLowerCase('es-PE')) && <p>Esa categoría ya existe. Reutilízala.</p>}
        <label><input type="checkbox" disabled={!canCreateCategory} checked={categoryConfirmed} onChange={event => setCategoryConfirmed(event.target.checked)} /> Confirmo crear esta referencia global, sin productos.</label><Button type="submit" disabled={!canCreateCategory || !categoryConfirmed}>Crear categoría global una vez</Button>
      </form>
    </Modal>
    <Modal open={task === 'owner' && !taskHidden} title="Propietario pendiente" onClose={() => { setTask(null); setOwnerEmail(''); setOwnerConfirmed(false); setDetailOpen(true) }} onRecoveryClose={() => setTaskHidden(true)} dirty={Boolean(p.initialOwner.data && ownerEmail.trim() !== p.initialOwner.data.email)} busy={p.busy} recovery={Boolean(p.pending)}>
      {p.error != null && <ApiErrorState error={p.error} />}<PlatformRecovery p={p} kind="operation" recovered={operationRecovered} />
      {p.initialOwner.data && <form className="operation-body" onSubmit={event => { event.preventDefault(); if (ownerConfirmed && !p.locked && p.initialOwner.data?.status === 'pending') { setOwnerConfirmed(false); void p.changeOwner(ownerEmail.trim()).then(saved => { if (saved) { setTask(null); setOwnerEmail(''); setDetailOpen(true) } }) } }}>
        <TextField label="Gmail del propietario pendiente" type="email" value={ownerEmail} maxLength={320} disabled={p.locked || p.initialOwner.data.status !== 'pending'} onChange={event => { setOwnerEmail(event.target.value); setOwnerConfirmed(false) }} />
        <p>Solo puede corregirse o revocarse antes del reclamo. No cambia al Owner ya vinculado a Google.</p>
        <label><input type="checkbox" disabled={p.locked || p.initialOwner.data.status !== 'pending'} checked={ownerConfirmed} onChange={event => setOwnerConfirmed(event.target.checked)} /> Confirmo el cambio o revocación del destinatario pendiente.</label>
        <div className="form-footer"><Button type="submit" disabled={p.locked || !ownerConfirmed || p.initialOwner.data.status !== 'pending' || !ownerEmail.trim() || ownerEmail.trim() === p.initialOwner.data.email}>Guardar Gmail pendiente</Button><Button variant="danger" disabled={p.locked || !ownerConfirmed || p.initialOwner.data.status !== 'pending'} onClick={() => { setOwnerConfirmed(false); void p.changeOwner(null).then(saved => { if (saved) { setTask(null); setOwnerEmail(''); setDetailOpen(true) } }) }}>Revocar asignación pendiente</Button></div>
      </form>}
    </Modal>
    <Modal open={task === 'license' && !taskHidden} title="Gestionar licencia" onClose={() => { setTask(null); setLicenseConfirmed(false); setExpires(''); setDetailOpen(true) }} onRecoveryClose={() => setTaskHidden(true)} dirty={Boolean(expires || licenseConfirmed)} busy={p.busy} recovery={Boolean(p.pending)}>
      {p.error != null && <ApiErrorState error={p.error} />}<PlatformRecovery p={p} kind="operation" recovered={operationRecovered} />
      {p.data && <form className="operation-body" onSubmit={event => { event.preventDefault(); if (!licenseConfirmed || p.locked || !p.data || !licenseActions(p.data.license.status)[action]) return; setLicenseConfirmed(false); void p.change(action, action === 'renew' ? { expiresAt: expires } : action === 'reactivate' ? { status } : {}).then(saved => { if (saved) { setTask(null); setExpires(''); setDetailOpen(true) } }) }}>
        <h3>{p.data.tenant.tradingName}</h3><label>Operación<select aria-label="Operación de licencia" value={action} disabled={p.locked} onChange={event => { setAction(event.target.value as LicenseAction); setLicenseConfirmed(false) }}><option value="renew" disabled={!licenseActions(p.data.license.status).renew}>Renovar</option><option value="suspend" disabled={!licenseActions(p.data.license.status).suspend}>Suspender</option><option value="reactivate" disabled={!licenseActions(p.data.license.status).reactivate}>Reactivar</option></select></label>
        {action === 'renew' && <UtcDateTimeField label="Nuevo vencimiento" value={expires} required disabled={p.locked} change={value => { setExpires(value); setLicenseConfirmed(false) }} />}
        {action === 'reactivate' && <label>Estado al reactivar<select aria-label="Estado al reactivar" value={status} disabled={p.locked} onChange={event => { setStatus(event.target.value as typeof status); setLicenseConfirmed(false) }}><option value="trial">Prueba gratuita</option><option value="active">Activa</option><option value="grace">Periodo de gracia</option></select></label>}
        <Notice>{action === 'suspend' ? 'Suspender bloquea las operaciones comerciales según las reglas del servidor. No borra registros.' : action === 'renew' ? 'Se conservará el historial y el servidor validará el nuevo vencimiento.' : 'El servidor comprobará si esta transición permite reactivar la licencia.'}</Notice>
        <label><input type="checkbox" disabled={p.locked} checked={licenseConfirmed} onChange={event => setLicenseConfirmed(event.target.checked)} /> Confirmo el cambio de la licencia seleccionada.</label><Button type="submit" disabled={p.locked || !licenseConfirmed || !licenseActions(p.data.license.status)[action] || action === 'renew' && !expires}>Aplicar cambio de licencia</Button>
      </form>}
    </Modal>
  </div>
}
