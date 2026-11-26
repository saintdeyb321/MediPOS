import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, Navigate, useLocation } from 'react-router'
import { ArrowRight, Building2, MapPin, Settings2 } from 'lucide-react'
import { accessMessage, ApiErrorState, branchDirectoryKey, useBranchDirectory, useRuntime, verifiedAccess } from '../access'
import type { AccessContext, Session } from '../access'
import { ApiError } from '../../shared/api/http'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Panel } from '../../shared/ui/Panel'
import { PageHeader } from '../../shared/ui/PageHeader'
import { PageControls } from '../../shared/ui/PageControls'
import { Notice } from '../../shared/ui/Notice'
import { Modal } from '../../shared/ui/Modal'
import { DateText } from '../../shared/ui/DateText'
import { licenseLabel } from '../../shared/ui/licenseLabel'
import { LoadingState } from '../../shared/ui/States'
import { useNavigationProtection } from '../../shared/ui/NavigationProtection'
import { settingsApi } from './api'
import type { BranchDetails, LegalEntity } from './api'
import { useSettingsCreation } from './useSettingsCreation'
import type { StructureRecord } from './useSettingsCreation'
import './structure.css'

export function TenantStructure({ access, session, tenantName, workspaceBase }: { access: AccessContext; session: Session; tenantName?: string; workspaceBase?: string }) {
  const { tenantId, membershipId } = access
  const { hash } = useLocation()
  const { http, service } = useRuntime()
  const scope = { tenantId }
  const root = `/boticas/${tenantId}/sucursales`
  const client = useQueryClient()
  const keys = ['tenant', tenantId, 'administration', session.userId, membershipId, session.expiresAtUtc] as const
  const branchKey = branchDirectoryKey(tenantId, session)
  const [offset, setOffset] = useState(0)
  const [legalName, setLegalName] = useState('')
  const [ruc, setRuc] = useState('')
  const [name, setName] = useState('')
  const [chosen, setChosen] = useState<LegalEntity | null>(null)
  const [newEntity, setNewEntity] = useState(false)
  const [task, setTask] = useState<'legal' | 'branch' | null>(null)
  const [entry, setEntry] = useState<BranchDetails | null>(null)
  const [notice, setNotice] = useState('')
  const [reviewed, setReviewed] = useState(false)
  const [observedRows, setObservedRows] = useState<StructureRecord[] | null>(null)
  const [candidateId, setCandidateId] = useState('')
  const api = http ? settingsApi(http) : null

  const branches = useBranchDirectory(api ? tenantId : '', session)
  const branchRows = branches.data ?? []
  const first = !workspaceBase && branches.isSuccess && !branchRows.length && !entry
  const write = useSettingsCreation(scope, session.userId, membershipId)
  const license = useQuery({ queryKey: [...keys, 'license'], queryFn: ({ signal }) => api!.license(scope, signal), enabled: Boolean(api), retry: false })
  const legalKey = [...keys, 'legal', offset]
  const legal = useQuery({ queryKey: legalKey, queryFn: ({ signal }) => api!.legal(scope, offset, signal),
    enabled: Boolean(api && (workspaceBase || branches.isSuccess || write.pending?.kind === 'legal')), retry: false })
  const canUseLicense = license.isSuccess && ['trial', 'active', 'grace'].includes(license.data.status)
  const atLimit = license.isSuccess && branchRows.length >= license.data.maxBranches
  const locked = write.locked || Boolean(entry) || !canUseLicense || access.role !== 'owner' || !access.membershipIsActive
  const entities = legal.data ?? []
  const selected = entities.find(item => item.id === write.entityId) ?? chosen ??
    (legal.isSuccess && offset === 0 && entities.length === 1 ? entities[0] : undefined)
  const createsLegal = first && !write.entityId && (newEntity || legal.isSuccess && offset === 0 && !entities.length && !selected)
  const needsFiscal = legal.isSuccess && offset === 0 && !entities.length && !selected
  const canCreate = !locked && !atLimit && branches.isSuccess && legal.isSuccess && !legal.isFetching && !branches.isFetching
  const validLegal = Boolean(legalName.trim() && ruc.trim())
  const entering = useQuery({
    queryKey: [...keys, 'new-branch-access', entry?.id],
    queryFn: ({ signal }) => service.access(tenantId, entry!.id, signal),
    enabled: Boolean(entry && !write.busy) && !write.pending, staleTime: 0, retry: false,
    refetchOnWindowFocus: false,
  })
  const entered = entry && verifiedAccess(entering.data, session, tenantId, entry.id)
  useNavigationProtection({ dirty: first && !write.busy && !write.pending && Boolean(legalName || ruc || name) })

  const showManagement = !first && Boolean(workspaceBase || branches.isSuccess)
  useEffect(() => {
    if (showManagement && ['#negocio', '#sucursales', '#plan'].includes(hash)) {
      const section = document.getElementById(hash.slice(1))
      section?.focus({ preventScroll: true })
      section?.scrollIntoView?.({ block: 'start' })
    }
  }, [hash, showManagement])

  function clearForm() { setLegalName(''); setRuc(''); setName(''); setChosen(null); setNewEntity(false); setTask(null) }
  async function saved(row: StructureRecord, continueWithBranch: boolean, enterAfter = continueWithBranch) {
    if ('legalName' in row) {
      setNotice(continueWithBranch ? 'Datos del negocio guardados. Falta crear tu sucursal.' : 'Datos guardados.')
      if (continueWithBranch) {
        setChosen(row)
        setLegalName('')
        setRuc('')
        setNewEntity(false)
      } else clearForm()
      await client.cancelQueries({ queryKey: [...keys, 'legal'] })
      client.setQueryData<LegalEntity[]>([...keys, 'legal', 0], old => old && old.length < 20 && !old.some(item => item.id === row.id) ? [...old, row] : old)
    } else {
      setNotice('Sucursal creada.')
      clearForm()
      if (enterAfter) setEntry(row)
      await client.cancelQueries({ queryKey: branchKey })
      client.setQueryData<BranchDetails[]>(branchKey, old => old?.some(item => item.id === row.id) ? old : [...(old ?? []), row])
    }
    setReviewed(false)
    setObservedRows(null)
    setCandidateId('')
  }
  function submit(onboarding: boolean, kind: 'legal' | 'branch') {
    if (locked || !api || kind === 'branch' && (!canCreate || (needsFiscal ? !validLegal : !selected) || !name.trim()) || kind === 'legal' && !validLegal) return
    const nextBranchName = (onboarding || task === 'branch') && kind === 'legal' ? name.trim() : undefined
    if ((onboarding || task === 'branch') && (!canCreate || !name.trim())) return
    const input = kind === 'legal' ? { legalName: legalName.trim(), ruc: ruc.trim() } : { legalEntityId: selected!.id, name: name.trim() }
    setNotice('')
    void write.run(input, {
      firstBranchName: nextBranchName,
      before: async signal => {
        await client.cancelQueries({ queryKey: kind === 'legal' ? [...keys, 'legal'] : branchKey })
        // A second tab can still hold an older “zero branches” view after the first tab releases the lock.
        if (onboarding) {
          const currentBranches = await service.branches(tenantId, signal)
          signal.throwIfAborted()
          client.setQueryData(branchKey, currentBranches)
          if (currentBranches.length) throw new ApiError(409, 'client.structure_changed')
        }
        if (nextBranchName && !newEntity) {
          const currentEntities = await api.legal(scope, 0, signal)
          signal.throwIfAborted()
          client.setQueryData([...keys, 'legal', 0], currentEntities)
          if (currentEntities.length) throw new ApiError(409, 'client.structure_changed')
        }
      },
      saved: row => saved(row, onboarding || Boolean(nextBranchName), onboarding),
    })
  }

  const eligible = (observedRows ?? []).filter(row => !write.pending?.legalEntityId || 'legalEntityId' in row && row.legalEntityId === write.pending.legalEntityId)
  const candidate = eligible.find(row => row.id === (write.pending?.acknowledgedId ?? candidateId))
  const recovery = write.pending && !write.busy && <Panel className="structure-recovery"><div className="operation-body">
    <h2>{write.pending.acknowledgedId ? 'Revisa el registro original' : 'No pudimos confirmar si se guardó'}</h2>
    <p>Revisa el resultado antes de volver a intentar. Comprueba el registro original y la fecha.</p>
    <Button data-recovery-focus variant="secondary" disabled={write.busy} onClick={() => {
      setObservedRows(null)
      setReviewed(false)
      void (write.pending?.kind === 'legal' ? legal.refetch() : branches.refetch()).then(result => setObservedRows(result.isSuccess ? result.data : null))
    }}>Consultar resultado</Button>
    {observedRows && !write.pending.acknowledgedId && <label>Registro que quieres verificar
      <select value={candidateId} onChange={event => { setCandidateId(event.target.value); setReviewed(false) }}>
        <option value="">Elige el registro original</option>
        {eligible.map(row => <option key={row.id} value={row.id}>{'legalName' in row ? row.legalName + ' · ' + row.ruc : row.name}</option>)}
      </select>
    </label>}
    {candidate && <>
      <p>Registro encontrado. {'legalName' in candidate ? candidate.legalName + ' · ' + candidate.ruc : candidate.name} · <DateText value={candidate.createdAt} /></p>
      <label><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Verifiqué que este es el registro de la solicitud original y que el envío terminó.</label>
      <Button disabled={!reviewed || write.busy || write.denied} onClick={() => void write.reconcile(candidate, !workspaceBase, row => saved(row, !workspaceBase))}>Confirmar resultado revisado</Button>
    </>}
  </div></Panel>
  const legalFields = <div className="form-section-fields">
    <TextField label="Razón social" hint="Nombre con el que estás registrado en SUNAT." required value={legalName} disabled={locked} onChange={e => setLegalName(e.target.value)} />
    <TextField label="RUC" required value={ruc} disabled={locked} onChange={e => setRuc(e.target.value)} hint="Usa el RUC de tu negocio." />
  </div>
  const branchField = <TextField label="Nombre de la sucursal" placeholder={first ? 'Ej. Sede principal' : undefined} autoComplete="off" required value={name} disabled={locked || atLimit} onChange={e => setName(e.target.value)} />
  const entityPager = (offset > 0 || entities.length === 20) && <PageControls offset={offset} count={entities.length}
    busy={write.busy || legal.isFetching} change={value => { setOffset(value); setReviewed(false); setObservedRows(null); setCandidateId('') }} />
  const entitySelect = <label>Datos fiscales de esta sucursal
    <select value={selected?.id ?? ''} disabled={locked || !legal.isSuccess || legal.isFetching || first && Boolean(write.entityId)}
      onChange={e => setChosen(entities.find(row => row.id === e.target.value) ?? null)}>
      <option value="">Elige la razón social de esta sucursal</option>
      {selected && !entities.some(row => row.id === selected.id) && <option value={selected.id}>{selected.legalName} · {selected.ruc}</option>}
      {entities.map(row => <option key={row.id} value={row.id}>{row.legalName} · {row.ruc}</option>)}
    </select>
  </label>
  const feedback = write.error != null && <ApiErrorState title="No pudimos completar el alta" error={write.error} />
  const licensePanel = <Panel id="plan" tabIndex={-1}><div className="operation-body">
    <h2>Plan y licencia</h2>
    {license.isPending && api && <p role="status">Consultando licencia…</p>}
    {license.isError && <ApiErrorState error={license.error} retry={() => void license.refetch()} />}
    {license.data && <dl className="summary-grid">
      <div><dt>Estado</dt><dd>{licenseLabel(license.data.status, license.data.expiresAt, access.evaluatedAtUtc)}</dd></div>
      <div><dt>Inicio</dt><dd><DateText value={license.data.startsAt} /></dd></div>
      <div><dt>Vence</dt><dd><DateText value={license.data.expiresAt} /></dd></div>
      <div><dt>Cupo</dt><dd>Hasta {license.data.maxBranches} sucursales</dd></div>
    </dl>}
    <p className="structure-note">Para cambiar el plan o renovar, contacta con soporte de MediPOS.</p>
  </div></Panel>

  if (entry && entering.isSuccess && entering.data.isAllowed && entered?.role === 'owner' && entered.membershipId === membershipId && !write.busy && !write.pending && !write.recoveryInAddress)
    return <Navigate to={`${root}/${entry.id}`} replace />

  return <div className={first ? 'operations structure structure-onboarding' : 'operations structure'}>
    <PageHeader icon={Settings2} eyebrow={first ? tenantName ?? '' : ''} title={first ? 'Configura tu botica' : workspaceBase ? 'Configuración' : 'Negocio y sucursales'} description={first ? 'Completa los datos del negocio y de tu primer local.' : undefined} />
    {showManagement && <nav className="structure-links" aria-label="Secciones de configuración"><a href="#negocio">Mi negocio</a><a href="#sucursales">Sucursales</a><a href="#plan">Plan y licencia</a>{workspaceBase && <><Link to={workspaceBase + '/auditoria'}>Auditoría</Link><Link to={workspaceBase + '/importacion'}>Importar Excel</Link></>}</nav>}
    {!api && <Notice tone="warning">Se requiere conexión HTTPS real.</Notice>}
    {(notice || first && write.entityId) && <p role="status" className="notice info">{notice || 'Datos del negocio guardados. Falta crear tu sucursal.'}</p>}
    {entry && <Notice>{entering.isPending || entering.isFetching ? 'Verificando acceso a la sucursal registrada…' :
      'Sucursal creada. Revisa el acceso para entrar.'}
      {entering.isError && <ApiErrorState error={entering.error} retry={() => void entering.refetch()} />}
      {entering.isSuccess && (!entering.data.isAllowed || entered?.membershipId !== membershipId || entered.role !== 'owner') && <>
        <p>{accessMessage(entering.data.isAllowed ? 'access.context_changed' : entering.data.code, 'owner')}</p>
        <Button variant="secondary" onClick={() => void entering.refetch()}>Comprobar acceso de nuevo</Button>
      </>}
    </Notice>}
    {!task && <>{feedback}{recovery}</>}
    {api && branches.isPending && <LoadingState label="Consultando sucursales…" />}
    {branches.isError && <ApiErrorState error={branches.error} retry={() => void branches.refetch()} />}
    {legal.isError && <ApiErrorState error={legal.error} retry={() => void legal.refetch()} />}
    {atLimit && <Notice tone="warning">Límite de sucursales de la licencia alcanzado.</Notice>}
    {first && <Panel><form className="operation-body structure-first" onSubmit={e => { e.preventDefault(); submit(true, createsLegal ? 'legal' : 'branch') }}>
      <div className="structure-first-fields">
        <fieldset><legend>Datos fiscales de tu negocio</legend>
          {legal.isPending ? <p role="status">Cargando datos fiscales…</p> : createsLegal ? legalFields : selected && (write.entityId || offset === 0 && entities.length === 1) ? <p><strong>{selected.legalName}</strong><br />RUC {selected.ruc}</p> : entitySelect}
          {entities.length > 0 && !write.entityId && <Button variant="quiet" disabled={locked} onClick={() => setNewEntity(!newEntity)}>{newEntity ? 'Usar datos fiscales guardados' : 'Registrar otros datos fiscales'}</Button>}
          {entityPager}
        </fieldset>
        <fieldset><legend>Tu primera sucursal</legend>{branchField}<p className="structure-note">Cada local tiene su propio inventario y caja.</p></fieldset>
      </div>
      <div className="structure-commit">
        {license.data && <p className="structure-note">{licenseLabel(license.data.status, license.data.expiresAt, access.evaluatedAtUtc)} · Hasta {license.data.maxBranches} {license.data.maxBranches === 1 ? 'sucursal' : 'sucursales'}</p>}
        <Button type="submit" pending={write.busy} disabled={!canCreate || !name.trim() || (createsLegal ? !validLegal : !selected)}>
          {write.busy ? write.saving === 'branch' ? 'Creando tu sucursal…' : 'Guardando los datos…' : 'Crear sucursal y comenzar'}{!write.busy && <ArrowRight aria-hidden="true" size={18} />}
        </Button>
      </div>
    </form></Panel>}
    {showManagement && <div className="structure-directories">
      <Panel id="negocio" tabIndex={-1}><div className="operation-body">
        <div className="structure-heading"><h2>Mi negocio</h2><Button variant="secondary" disabled={locked || !legal.isSuccess} onClick={() => setTask('legal')}>{entities.length ? 'Registrar otros datos fiscales' : 'Registrar datos del negocio'}</Button></div>
        {legal.isFetching && <p role="status">Cargando datos fiscales…</p>}
        {legal.isSuccess && !entities.length && <p>{offset ? 'Esta página está vacía.' : 'Aún no has registrado los datos fiscales de tu negocio.'}</p>}
        <ul className="structure-records">{entities.map(row => <li key={row.id}><Building2 aria-hidden="true" size={22} /><div><strong>{row.legalName}</strong><p>{row.ruc}</p></div></li>)}</ul>
        {entityPager}
      </div></Panel>
      <Panel id="sucursales" tabIndex={-1}><div className="operation-body">
        <div className="structure-heading"><h2>Sucursales</h2>{!atLimit && <Button disabled={!canCreate} onClick={() => setTask('branch')}>Nueva sucursal</Button>}</div>
        {license.data && branches.isSuccess && branchRows.length < 100 && <p className="structure-note">{branchRows.length} de {license.data.maxBranches} sucursales</p>}
        {branches.isSuccess && !branchRows.length && <p>Crea una sucursal para comenzar a trabajar.</p>}
        <ul className="structure-records">{branchRows.map(row => <li key={row.id}><MapPin aria-hidden="true" size={22} /><div>
          <strong>{row.name}</strong>{row.isMainHub && <Badge tone="info">Sede principal</Badge>}
          <p>{entities.find(item => item.id === row.legalEntityId)?.legalName ?? 'Datos fiscales vinculados'}</p>
          {workspaceBase === `${root}/${row.id}` ? <Badge>Sede actual</Badge> : <Link to={`${root}/${row.id}`}>Entrar</Link>}
        </div></li>)}</ul>
        {branchRows.length >= 100 && <p>Se muestran hasta 100 sucursales.</p>}
      </div></Panel>
    </div>}
    {!first ? licensePanel : license.isError && <ApiErrorState error={license.error} retry={() => void license.refetch()} />}
    {task && <Modal open title={task === 'legal' ? 'Registrar datos del negocio' : 'Crear sucursal'} busy={write.busy}
      dirty={Boolean(legalName || ruc || name)}
      recovery={Boolean(write.pending)} onRecoveryClose={() => setTask(null)} onClose={clearForm}>
      <form onSubmit={e => { e.preventDefault(); submit(false, task === 'branch' && needsFiscal ? 'legal' : task) }} className="operation-body">
        {feedback}{recovery}
        {task === 'legal' ? legalFields : <>{needsFiscal ? legalFields : selected && offset === 0 && entities.length === 1 ? <p><strong>{selected.legalName}</strong><br />RUC {selected.ruc}</p> : entitySelect}{branchField}{entityPager}</>}
        <Button type="submit" disabled={task === 'legal' ? locked || !validLegal : !canCreate || (needsFiscal ? !validLegal : !selected) || !name.trim()}>
          {task === 'legal' ? 'Guardar datos fiscales' : 'Crear sucursal'}
        </Button>
      </form>
    </Modal>}
  </div>
}
