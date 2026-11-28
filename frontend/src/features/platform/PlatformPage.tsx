import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { Building2, CircleUserRound, KeyRound, Layers3, ShieldCheck, UserRoundCheck } from 'lucide-react'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Notice } from '../../shared/ui/Notice'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Modal } from '../../shared/ui/Modal'
import { DateText } from '../../shared/ui/DateText'
import { licenseLabel } from '../../shared/ui/licenseLabel'
import { licensePlanMonths } from './api'
import type { LicenseAction, LicenseChangeInput, PlanMonths } from './api'
import { usePlatformAdmission } from './usePlatformAdmission'
import { categoryNameKey, usePlatformConsole } from './usePlatformConsole'
import { NewTenantForm } from './NewTenantDialog'
import { PlatformAccount, PlatformCategories, PlatformHome } from './PlatformAreas'
import { PlatformReadError } from './PlatformAdmissionError'
import { PlatformTenantDirectory } from './PlatformTenantDirectory'
import { PlatformTenantDetails } from './PlatformTenantDetails'
import { PlatformRecovery } from './PlatformRecovery'
import { licenseActions, platformDestination } from './presentation'
import '../../shared/styles/operations.css'

const sections = {
  home: { title: 'Inicio de plataforma', description: 'Revisa las boticas, sus asignaciones y los próximos vencimientos.', icon: ShieldCheck },
  businesses: { title: 'Boticas', description: 'Consulta cada negocio y gestiona su alta.', icon: Building2 },
  owners: { title: 'Propietarios pendientes', description: 'El propietario reclama el acceso al ingresar con el Gmail designado y verificado.', icon: UserRoundCheck },
  licenses: { title: 'Licencias', description: 'Consulta vigencia, estado y cupo de sucursales.', icon: KeyRound },
  catalog: { title: 'Catálogo global', description: 'Categorías compartidas como referencia por las boticas.', icon: Layers3 },
  account: { title: 'Mi cuenta', description: 'Tu sesión y alcance administrativo.', icon: CircleUserRound },
} as const
type Task = 'create' | 'category' | 'owner' | 'license'

// A switch of administrative area keeps the same operation coordinator mounted.
export function PlatformPage() {
  const { admission, http, session } = usePlatformAdmission('cached')
  return http && session.data ? <PlatformConsole key={session.data.userId + (session.data.expiresAtUtc ?? '')} http={http}
    userId={session.data.userId} serverTime={session.data.serverTimeUtc} expiresAt={session.data.expiresAtUtc ?? undefined} admitted={admission.isSuccess} /> : null
}
function PlatformConsole({ http, userId, expiresAt, serverTime, admitted }: { http: NonNullable<ReturnType<typeof usePlatformAdmission>['http']>; userId: string; expiresAt?: string; serverTime?: string; admitted: boolean }) {
  const [params] = useSearchParams()
  const area = params.get('area') ?? 'home'
  const current = sections[area as keyof typeof sections]
  const inDirectory = ['home', 'businesses', 'owners', 'licenses'].includes(area)
  const selected = params.get('tenant')
  const [drawer, setDrawer] = useState(Boolean(selected))
  const [task, setTask] = useState<Task | null>(null)
  const [taskHidden, setTaskHidden] = useState(false)
  const open = task !== null && !taskHidden
  const detailOpen = drawer && inDirectory && Boolean(selected)
  const p = { ...usePlatformConsole(http, userId, expiresAt, area, admitted, detailOpen || open && (task === 'owner' || task === 'license'), detailOpen || open && task === 'owner'), serverTime }
  const [createDirty, setCreateDirty] = useState(false)
  const [createEpoch, setCreateEpoch] = useState(0)
  const [ownerEmail, setOwnerEmail] = useState('')
  const [revoking, setRevoking] = useState(false)
  const [action, setAction] = useState<LicenseAction>('renew')
  const [termMonths, setTermMonths] = useState<PlanMonths | ''>('')
  const [status, setStatus] = useState<'trial' | 'active' | 'grace'>('active')
  const [categoryName, setCategoryName] = useState('')

  function openTask(next: Task) { setTask(next); setTaskHidden(false); setDrawer(false) }
  function dismissCreate() { setTask(null); setTaskHidden(false); setCreateDirty(false); setCreateEpoch(value => value + 1) }
  function created() { dismissCreate(); setDrawer(true) }
  function operationRecovered() { setTask(null); setTaskHidden(false); setOwnerEmail(''); setTermMonths(''); setRevoking(false); setDrawer(true) }
  function categoryRecovered() { setTask(null); setTaskHidden(false); setCategoryName('') }
  function manage(next: 'owner' | 'license') {
    if (next === 'owner') {
      setOwnerEmail(p.initialOwner.data?.email ?? '')
      setRevoking(false)
    } else if (p.data) {
      const allowed = licenseActions(p.data.license.status)
      setAction(allowed.reactivate ? 'reactivate' : 'renew')
      setStatus('active')
    }
    openTask(next)
  }
  const select = (id: string) => { p.select(id); setDrawer(true) }
  const relevantError = <PlatformReadError error={p.error} failedAt={p.failedAt} fetching={p.busy} />
  const hydratedCategory = p.categoriesRead && p.categories.data && p.categories.data.length < 100
  const duplicate = p.categories.data?.some(row => categoryNameKey(row.name) === categoryNameKey(categoryName))
  const canCreateCategory = !p.locked && hydratedCategory && categoryName.trim().length > 0 && categoryName.trim().length <= 128 && !duplicate
  const owner = p.initialOwner.data
  const canEditOwner = !p.locked && p.data && !p.data.initialOwnerUserId && p.initialOwner.isSuccess && !p.initialOwner.isFetching && owner?.status === 'pending'
  const changedAddress = Boolean(ownerEmail.trim() && ownerEmail.trim().toLowerCase() !== owner?.email.toLowerCase())
  const actions = p.data && licenseActions(p.data.license.status)
  const canChange = !p.locked && actions?.[action] && (action !== 'renew' || Boolean(termMonths))
  if (p.denied) return <Notice tone="error">La autoridad de plataforma ya no está disponible. Vuelve a ingresar o solicita su revisión.</Notice>
  return <div className="operations platform-console">
    <PageHeader title={current?.title ?? 'Plataforma'} eyebrow="" icon={current?.icon}
      action={['home', 'businesses'].includes(area) && <Button disabled={p.locked} onClick={() => openTask('create')}>Nueva botica</Button>} />
    {p.notice && !detailOpen && <Notice><span role="status">{p.notice}</span></Notice>}
    {!open && !detailOpen && relevantError}
    {(!open || task !== 'create') && <PlatformRecovery key={p.creationKey ?? 'creation'} p={p} kind="creation" recovered={created} />}
    {(!open || task !== 'owner' && task !== 'license') && <PlatformRecovery key={(p.pending ?? '') + p.tenantId} p={p} kind="operation" recovered={operationRecovered} />}
    {(!open || task !== 'category') && <PlatformRecovery key={p.categoryPending ?? 'category'} p={p} kind="category" recovered={categoryRecovered} />}
    {task && taskHidden && (p.pending || p.creationKey || p.categoryPending) && <Button data-recovery-focus variant="secondary" disabled={p.busy} onClick={() => setTaskHidden(false)}>Mostrar la solicitud original</Button>}

    {area === 'home' && <PlatformHome p={p} destination={next => platformDestination(params, area, next)} />}
    {inDirectory && area !== 'home' && <PlatformTenantDirectory key={area} p={p} area={area} select={select} />}
    {area === 'catalog' && <PlatformCategories p={p} create={() => openTask('category')} />}
    {area === 'account' && <PlatformAccount expiresAt={expiresAt} />}
    {!current && <Notice>Selecciona un área desde el menú de plataforma.</Notice>}
    <PlatformTenantDetails p={p} open={detailOpen} close={() => setDrawer(false)} manage={manage} />

    <Modal open={open && task === 'create'} wide title="Nueva botica" onClose={dismissCreate} onRecoveryClose={() => setTaskHidden(true)} dirty={createDirty} busy={p.busy} recovery={Boolean(p.creationKey)}>
      {relevantError}<PlatformRecovery p={p} kind="creation" recovered={created} />
      <NewTenantForm key={createEpoch} locked={p.locked} create={async input => { const saved = await p.create(input); if (saved) created(); return saved }} dirty={setCreateDirty} />
    </Modal>
    <Modal open={open && task === 'category'} title="Crear categoría global" onClose={categoryRecovered} onRecoveryClose={() => setTaskHidden(true)} dirty={Boolean(categoryName)} busy={p.busy} recovery={Boolean(p.categoryPending)}>
      {relevantError}<PlatformRecovery p={p} kind="category" recovered={categoryRecovered} />
      <form className="operation-body" onSubmit={event => { event.preventDefault(); if (canCreateCategory) { void p.createCategory(categoryName.trim()).then(saved => { if (saved) categoryRecovered() }) } }}>
        <TextField label="Nombre de la categoría global" value={categoryName} maxLength={128} disabled={p.locked} onChange={event => setCategoryName(event.target.value)} />
        {duplicate && <p>Esa categoría ya existe.</p>}
        <Button type="submit" disabled={!canCreateCategory}>Crear categoría</Button>
      </form>
    </Modal>
    <Modal open={open && task === 'owner'} title="Propietario pendiente" onClose={operationRecovered} onRecoveryClose={() => setTaskHidden(true)} dirty={Boolean(owner && changedAddress)} busy={p.busy} recovery={Boolean(p.pending)}>
      {relevantError}<PlatformRecovery p={p} kind="operation" recovered={operationRecovered} />
      <PlatformReadError error={p.detail.error} failedAt={p.detail.errorUpdatedAt} fetching={p.detail.isFetching} retry={admitted ? () => void p.detail.refetch() : undefined} />
      <PlatformReadError error={p.initialOwner.error} failedAt={p.initialOwner.errorUpdatedAt} fetching={p.initialOwner.isFetching} retry={admitted && p.detail.isSuccess ? () => void p.initialOwner.refetch() : undefined} />
      {owner && <form className="operation-body" onSubmit={event => { event.preventDefault(); if (canEditOwner && changedAddress && !revoking) { void p.changeOwner(ownerEmail.trim()).then(saved => { if (saved) operationRecovered() }) } }}>
        {revoking ? <><Notice tone="warning">Revocarás el acceso pendiente de {owner.email} a {p.data?.tenant.tradingName ?? 'esta botica'}.</Notice>
          <div className="form-footer"><Button variant="secondary" onClick={() => setRevoking(false)}>Volver a editar</Button><Button variant="danger" disabled={!canEditOwner} onClick={() => void p.changeOwner(null).then(saved => { if (saved) operationRecovered() })}>Confirmar revocación</Button></div></> :
          <><TextField label="Gmail del propietario pendiente" type="email" value={ownerEmail} maxLength={320} disabled={!canEditOwner} onChange={event => setOwnerEmail(event.target.value)} />
            <p>La corrección o revocación solo está disponible antes de que el propietario reclame la asignación.</p>
            <div className="form-footer"><Button type="submit" disabled={!canEditOwner || !changedAddress}>Guardar Gmail pendiente</Button><Button variant="secondary" disabled={!canEditOwner} onClick={() => setRevoking(true)}>Revocar asignación pendiente</Button></div></>}
      </form>}
    </Modal>
    <Modal open={open && task === 'license'} title="Gestionar licencia" onClose={operationRecovered} onRecoveryClose={() => setTaskHidden(true)} dirty={Boolean(termMonths)} busy={p.busy} recovery={Boolean(p.pending)}>
      {relevantError}<PlatformRecovery p={p} kind="operation" recovered={operationRecovered} />
      <PlatformReadError error={p.detail.error} failedAt={p.detail.errorUpdatedAt} fetching={p.detail.isFetching} retry={admitted ? () => void p.detail.refetch() : undefined} />
      {p.data && actions && <form className="operation-body" onSubmit={event => {
        event.preventDefault()
        if (!canChange || !p.data) return
        const input: LicenseChangeInput = action === 'renew' && termMonths ? { termMonths, expectedExpiresAt: p.data.license.expiresAt } : action === 'reactivate' ? { status } : {}
        void p.change(action, input).then(saved => { if (saved) operationRecovered() })
      }}>
        <div><h3>{p.data.tenant.tradingName}</h3><p>Licencia {licenseLabel(p.data.license.status, p.data.license.expiresAt, serverTime)} · vence <DateText value={p.data.license.expiresAt} /></p></div>
        <label>Operación<select aria-label="Operación de licencia" value={action} disabled={p.locked} onChange={event => setAction(event.target.value as LicenseAction)}>
          <option value="renew" disabled={!actions.renew}>Renovar</option><option value="suspend" disabled={!actions.suspend}>Suspender</option><option value="reactivate" disabled={!actions.reactivate}>Reactivar</option>
        </select></label>
        {action === 'renew' && <><label>Periodo de renovación<select value={termMonths} required disabled={p.locked} onChange={event => {
          const value = event.target.value
          setTermMonths(value === '' ? '' : Number(value) as PlanMonths)
        }}>
          <option value="">Selecciona un periodo</option>
          {licensePlanMonths.map(months => <option key={months} value={months}>{months} {months === 1 ? 'mes' : 'meses'}</option>)}
        </select></label>
          <p>El periodo se suma desde el vencimiento o desde hoy si ya venció. Renovar conserva el estado de la licencia.</p></>}
        {action === 'reactivate' && <label>Estado al reactivar<select aria-label="Estado al reactivar" value={status} disabled={p.locked} onChange={event => setStatus(event.target.value as typeof status)}><option value="trial">Prueba gratuita</option><option value="active">Activa</option><option value="grace">Periodo de gracia</option></select></label>}
        {action === 'suspend' && <Notice tone="warning">Suspender {p.data.tenant.tradingName} restringe sus operaciones comerciales.</Notice>}
        <Button type="submit" variant={action === 'suspend' ? 'danger' : 'primary'} disabled={!canChange}>{action === 'suspend' ? 'Suspender licencia' : action === 'renew' ? 'Renovar licencia' : 'Reactivar licencia'}</Button>
      </form>}
    </Modal>
  </div>
}
