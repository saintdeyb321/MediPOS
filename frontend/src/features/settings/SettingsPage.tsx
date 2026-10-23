import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Panel } from '../../shared/ui/Panel'
import { PageHeader } from '../../shared/ui/PageHeader'
import { PageControls } from '../../shared/ui/PageControls'
import { Notice } from '../../shared/ui/Notice'
import { Modal } from '../../shared/ui/Modal'
import { DateText } from '../../shared/ui/DateText'
import { Steps } from '../../shared/ui/Steps'
import { licenseLabel } from '../../shared/ui/licenseLabel'
import { settingsApi } from './api'
import type { TenantSettingsScope } from './api'
import { useSettingsCreation } from './useSettingsCreation'
import '../../shared/styles/operations.css'

export function SettingsPage() {
  const w = useWorkspace()
  if (w.access.role !== 'owner') return <Panel role="alert"><h1>Esta sección requiere otro acceso</h1><p>Configuración requiere Owner.</p></Panel>
  return <TenantSettings key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} scope={w} userId={w.session.userId} membershipId={w.access.membershipId} workspaceBase={w.base} />
}
export function TenantSettings({ scope: w, userId, membershipId, workspaceBase, title = 'Configuración' }: { scope: TenantSettingsScope; userId: string; membershipId: string; workspaceBase?: string; title?: string }) {
  const { http } = useRuntime(); const client = useQueryClient()
  const [legalOffset, setLegalOffset] = useState(0); const [branchOffset, setBranchOffset] = useState(0); const [legalName, setLegalName] = useState(''); const [ruc, setRuc] = useState(''); const [name, setName] = useState(''); const [entityId, setEntityId] = useState('')
  const [legalConfirm, setLegalConfirm] = useState(false); const [branchConfirm, setBranchConfirm] = useState(false); const [reconciled, setReconciled] = useState(false); const [candidateId, setCandidateId] = useState('')
  const [section, setSection] = useState<'business' | 'legal' | 'branches' | 'license'>(workspaceBase ? 'business' : 'legal')
  const [task, setTask] = useState<'legal' | 'branch' | null>(null)
  const api = () => { if (!http) throw new ApiError(0, 'client.https_required'); return settingsApi(http) }
  const key = (resource: string, ...args: unknown[]) => ['tenant', w.tenantId, 'administration', userId, membershipId, resource, ...args]
  const license = useQuery({ queryKey: key('settings-license'), queryFn: ({ signal }) => api().license(w, signal), enabled: Boolean(http), retry: false, refetchOnWindowFocus: false })
  const legal = useQuery({ queryKey: key('settings-legal', legalOffset), queryFn: ({ signal }) => api().legal(w, legalOffset, signal), enabled: Boolean(http), retry: false, refetchOnWindowFocus: false })
  const branches = useQuery({ queryKey: key('settings-branches', branchOffset), queryFn: ({ signal }) => api().branches(w, branchOffset, signal), enabled: Boolean(http), retry: false, refetchOnWindowFocus: false })
  const refresh = () => client.invalidateQueries({ predicate: q => q.queryKey[0] === 'tenant' && q.queryKey[1] === w.tenantId && ['branches', 'administration'].includes(String(q.queryKey[2])) })
  const write = useSettingsCreation(w, refresh)
  const licenseData = license.isSuccess && !license.isFetching ? license.data : null
  const entities = legal.isSuccess && !legal.isFetching ? legal.data : null; const branchRows = branches.isSuccess && !branches.isFetching ? branches.data : null
  const atLimit = Boolean(licenseData && branchOffset === 0 && branchRows && branchRows.length >= licenseData.maxBranches)
  const selected = entities?.find(row => row.id === entityId)
  const candidate = write.pending === 'legal' ? entities?.find(row => row.id === (write.createdId || candidateId)) : branchRows?.find(row => row.id === (write.createdId || candidateId))
  const locked = write.locked || !http || !licenseData || !['trial', 'active', 'grace'].includes(licenseData.status)
  const recovery = write.pending && <Notice tone="warning"><div className="operation-body"><h3>Alta pendiente de conciliación</h3><p>No repitas la solicitud. La ausencia de una fila o un nombre coincidente no demuestran el resultado original.</p><Button disabled={write.busy} onClick={() => void refresh()}>Reconsultar directorios originales</Button>
    {!write.createdId && <label>Registro original<select aria-label="Registro original a revisar en la tarea" value={candidateId} onChange={e => { setCandidateId(e.target.value); setReconciled(false) }}><option value="">Selecciona tras verificar la operación</option>{write.pending === 'legal' ? entities?.map(row => <option key={row.id} value={row.id}>{row.legalName} · {row.ruc}</option>) : branchRows?.map(row => <option key={row.id} value={row.id}>{row.name} · {entities?.find(entity => entity.id === row.legalEntityId)?.legalName ?? 'Entidad no disponible en esta página'}</option>)}</select></label>}
    {candidate && <><p>{'legalName' in candidate ? candidate.legalName + ' · ' + candidate.ruc : candidate.name + ' · ' + (entities?.find(entity => entity.id === candidate.legalEntityId)?.legalName ?? 'Entidad no disponible en esta página')} · <DateText value={candidate.createdAt} /></p><label><input type="checkbox" checked={reconciled} onChange={e => setReconciled(e.target.checked)} /> Verifiqué identidad, atributos y finalización originales, sin otra solicitud en curso.</label><Button disabled={!reconciled || write.busy} onClick={() => { write.reconcile(); setReconciled(false); setCandidateId(''); setLegalConfirm(false); setBranchConfirm(false) }}>Continuar tras conciliación</Button></>}
    <PageControls offset={write.pending === 'legal' ? legalOffset : branchOffset} count={write.pending === 'legal' ? entities?.length : branchRows?.length} busy={write.busy || legal.isFetching || branches.isFetching} change={value => { if (write.pending === 'legal') setLegalOffset(value); else setBranchOffset(value); setCandidateId(''); setReconciled(false) }} refresh={() => void refresh()} />
  </div></Notice>
  return <div className="operations"><PageHeader title={title} description="Datos del negocio, entidades, sucursales y licencia." />
    {!workspaceBase && <Steps labels={['Registrar entidad', 'Crear sucursal', 'Seleccionar sucursal', 'Comenzar']} current={branchRows?.length ? 2 : entities?.length ? 1 : 0} label="Configuración inicial de la botica" />}
    <nav className="settings-index" aria-label="Secciones de configuración">{([['business', 'Datos del negocio'], ['legal', 'Entidades legales'], ['branches', 'Sucursales'], ['license', 'Licencia']] as const).map(([id, label]) => <Button key={id} variant="secondary" aria-pressed={section === id} onClick={() => setSection(id)}>{label}</Button>)}</nav>
    {workspaceBase && <nav><Link to={workspaceBase + '/auditoria'}>Historial de auditoría</Link> · <Link to={workspaceBase + '/importacion'}>Importar productos Excel</Link></nav>}
    {!http && <Notice tone="warning">Se requiere conexión HTTPS real.</Notice>}
    <Panel hidden={section !== 'license' && section !== 'business'}><div className="operation-body"><h2>Licencia actual</h2>{license.isFetching && <p role="status">Consultando licencia…</p>}{license.isError && <ApiErrorState error={license.error} retry={() => void license.refetch()} />}{licenseData && <dl className="summary-grid"><div><dt>Estado</dt><dd>{licenseLabel(licenseData.status)}</dd></div><div><dt>Inicio</dt><dd><DateText value={licenseData.startsAt} /></dd></div><div><dt>Vencimiento</dt><dd><DateText value={licenseData.expiresAt} /></dd></div><div><dt>Máximo de sucursales</dt><dd>{licenseData.maxBranches}</dd></div></dl>}<Button variant="secondary" onClick={() => void refresh()} disabled={write.busy}>Actualizar configuración</Button></div></Panel>
    {write.error != null && <ApiErrorState error={write.error} />}{write.notice && <p role="status">{write.notice}</p>}
    {write.pending && <Panel><div className="operation-body"><h2>Alta pendiente de conciliación</h2><p>No repitas la solicitud. Reconsulta los directorios: una fila ausente o un nombre coincidente no demuestran el resultado. No hay idempotencia por nombre o RUC.</p>
      {!write.createdId && <label>Registro original a revisar<select aria-label="Registro original a revisar" value={candidateId} onChange={e => { setCandidateId(e.target.value); setReconciled(false) }}><option value="">Selecciona tras verificar la operación original</option>{write.pending === 'legal' ? entities?.map(row => <option key={row.id} value={row.id}>{row.legalName} · {row.ruc}</option>) : branchRows?.map(row => <option key={row.id} value={row.id}>{row.name} · {row.legalEntityId}</option>)}</select></label>}
      {candidate && <><p>Registro reconsultado: {'legalName' in candidate ? candidate.legalName + ' · ' + candidate.ruc : candidate.name + ' · entidad ' + candidate.legalEntityId} · UTC {candidate.createdAt}</p><label><input type="checkbox" checked={reconciled} onChange={e => setReconciled(e.target.checked)} /> Verifiqué inequívocamente identidad, atributos y finalización de la solicitud original; no hay otra solicitud en curso.</label><Button disabled={!reconciled || write.busy} onClick={() => { write.reconcile(); setReconciled(false); setCandidateId(''); setLegalConfirm(false); setBranchConfirm(false) }}>Continuar tras conciliación</Button></>}
    </div></Panel>}
    <Panel hidden={section !== 'legal' && section !== 'business'}><div className="operation-body"><div className="operation-actions"><h2>Entidades legales</h2><Button disabled={locked} onClick={() => setTask('legal')}>Nueva entidad</Button></div>{legal.isFetching && <p role="status">Consultando entidades…</p>}{legal.isError && <ApiErrorState error={legal.error} retry={() => void legal.refetch()} />}{entities && <>{!entities.length && <p>No hay entidades en esta página.</p>}<ul>{entities.map(row => <li key={row.id}>{row.legalName} · Identificador {row.ruc}</li>)}</ul></>}<PageControls offset={legalOffset} count={entities?.length} busy={write.busy || legal.isFetching} change={offset => { setLegalOffset(offset); setEntityId(''); setBranchConfirm(false); setReconciled(false) }} refresh={() => void legal.refetch()} />
    </div></Panel>
    <Modal open={task === 'legal'} title="Registrar entidad legal" onClose={() => { setTask(null); setLegalName(''); setRuc(''); setLegalConfirm(false) }} onRecoveryClose={() => setTask(null)} dirty={Boolean(legalName || ruc || legalConfirm)} busy={write.busy} recovery={Boolean(write.pending)}>
      <div className="operation-body">{write.error != null && <ApiErrorState error={write.error} />}{write.notice && <p role="status">{write.notice}</p>}{recovery}
      <form onSubmit={e => { e.preventDefault(); if (legalConfirm && !locked) { setLegalConfirm(false); void write.run({ legalName: legalName.trim(), ruc: ruc.trim() }) } }}><h3>Crear entidad legal</h3><TextField label="Razón social" required value={legalName} disabled={locked} onChange={e => { setLegalName(e.target.value); setLegalConfirm(false) }} /><TextField label="Identificador de la entidad" required value={ruc} disabled={locked} onChange={e => { setRuc(e.target.value); setLegalConfirm(false) }} /><p>Usa el RUC verificado. Solo en QA aislado autorizado puedes usar un identificador explícitamente no fiscal, que no representa un RUC válido ni habilita comprobantes tributarios.</p><label><input type="checkbox" checked={legalConfirm} disabled={locked} onChange={e => setLegalConfirm(e.target.checked)} /> Confirmo la creación persistente de {legalName || 'esta entidad'} con el RUC indicado.</label><Button type="submit" disabled={locked || !legalConfirm || !legalName.trim() || !ruc.trim()}>Crear entidad legal</Button></form>
      </div>
    </Modal>
    <Panel hidden={section !== 'branches' && section !== 'business'}><div className="operation-body"><div className="operation-actions"><h2>Sucursales</h2><Button disabled={locked || atLimit} onClick={() => setTask('branch')}>Nueva sucursal</Button></div>{branches.isFetching && <p role="status">Consultando sucursales…</p>}{branches.isError && <ApiErrorState error={branches.error} retry={() => void branches.refetch()} />}{branchRows && <>{!branchRows.length && <p>No hay sucursales en esta página.</p>}<ul>{branchRows.map(row => <li key={row.id}>{row.name} · Entidad: {entities?.find(entity => entity.id === row.legalEntityId)?.legalName ?? 'Entidad no disponible en esta página'}{row.isMainHub ? ' · Sede principal' : ''}</li>)}</ul></>}<PageControls offset={branchOffset} count={branchRows?.length} busy={write.busy || branches.isFetching} change={offset => { setBranchOffset(offset); setReconciled(false) }} refresh={() => void branches.refetch()} />
      {atLimit && <Notice tone="warning">Límite de sucursales de la licencia alcanzado. El servidor vuelve a comprobarlo dentro de la creación.</Notice>}
      {!workspaceBase && branchRows && branchRows.length > 0 && <Link to={`/boticas/${w.tenantId}/sucursales`}>Elegir sucursal y entrar al espacio de trabajo</Link>}
    </div></Panel>
    <Modal open={task === 'branch'} title="Crear sucursal" onClose={() => { setTask(null); setName(''); setEntityId(''); setBranchConfirm(false) }} onRecoveryClose={() => setTask(null)} dirty={Boolean(name || entityId || branchConfirm)} busy={write.busy} recovery={Boolean(write.pending)}>
      <div className="operation-body">{write.error != null && <ApiErrorState error={write.error} />}{write.notice && <p role="status">{write.notice}</p>}{recovery}
      {!entities?.length && <Notice tone="warning">Registra primero una entidad legal o consulta la página de entidades que corresponda.</Notice>}
      <form onSubmit={e => { e.preventDefault(); if (branchConfirm && selected && !locked && !atLimit) { setBranchConfirm(false); void write.run({ legalEntityId: selected.id, name: name.trim() }) } }}><h3>Crear sucursal</h3><TextField label="Nombre de la nueva sucursal" value={name} required disabled={locked || atLimit} onChange={e => { setName(e.target.value); setBranchConfirm(false) }} /><label>Entidad legal existente<select aria-label="Entidad legal existente" value={entityId} disabled={locked || atLimit || !entities} onChange={e => { setEntityId(e.target.value); setBranchConfirm(false) }}><option value="">Selecciona en la página de entidades consultada</option>{entities?.map(row => <option key={row.id} value={row.id}>{row.legalName} · {row.ruc}</option>)}</select></label><label><input type="checkbox" checked={branchConfirm} disabled={locked || atLimit} onChange={e => setBranchConfirm(e.target.checked)} /> Confirmo crear {name || 'esta sucursal'} para {selected?.legalName ?? 'la entidad seleccionada'}.</label><Button type="submit" disabled={locked || atLimit || !branchConfirm || !selected || !name.trim()}>Crear sucursal</Button></form>
      </div>
    </Modal></div>
}
