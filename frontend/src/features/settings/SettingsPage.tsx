import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { ApiError } from '../../shared/api/http'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Panel } from '../../shared/ui/Panel'
import { PageHeader } from '../../shared/ui/PageHeader'
import { PageControls } from '../../shared/ui/PageControls'
import { Notice } from '../../shared/ui/Notice'
import { settingsApi } from './api'
import { useSettingsCreation } from './useSettingsCreation'
import '../../shared/styles/operations.css'

export function SettingsPage() {
  const w = useWorkspace()
  if (w.access.role !== 'owner') return <Panel role="alert"><h1>Esta sección requiere otro acceso</h1><p>Configuración requiere Owner.</p></Panel>
  return <SettingsWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function SettingsWorkspace() {
  const w = useWorkspace(); const { http } = useRuntime(); const client = useQueryClient()
  const [legalOffset, setLegalOffset] = useState(0); const [branchOffset, setBranchOffset] = useState(0); const [legalName, setLegalName] = useState(''); const [ruc, setRuc] = useState(''); const [name, setName] = useState(''); const [entityId, setEntityId] = useState('')
  const [legalConfirm, setLegalConfirm] = useState(false); const [branchConfirm, setBranchConfirm] = useState(false); const [reconciled, setReconciled] = useState(false); const [candidateId, setCandidateId] = useState('')
  const api = () => { if (!http) throw new ApiError(0, 'client.https_required'); return settingsApi(http) }
  const key = (resource: string, ...args: unknown[]) => scopedKey(w, resource, w.session.userId, w.access.membershipId, ...args)
  const license = useQuery({ queryKey: key('settings-license'), queryFn: ({ signal }) => api().license(w, signal), enabled: Boolean(http), retry: false, refetchOnWindowFocus: false })
  const legal = useQuery({ queryKey: key('settings-legal', legalOffset), queryFn: ({ signal }) => api().legal(w, legalOffset, signal), enabled: Boolean(http), retry: false, refetchOnWindowFocus: false })
  const branches = useQuery({ queryKey: key('settings-branches', branchOffset), queryFn: ({ signal }) => api().branches(w, branchOffset, signal), enabled: Boolean(http), retry: false, refetchOnWindowFocus: false })
  const refresh = () => client.invalidateQueries({ predicate: q => q.queryKey[0] === 'tenant' && q.queryKey[1] === w.tenantId && (q.queryKey[2] === 'branches' || ['settings-license', 'settings-legal', 'settings-branches'].includes(String(q.queryKey[3]))) })
  const write = useSettingsCreation(refresh)
  const licenseData = license.isSuccess && !license.isFetching ? license.data : null
  const entities = legal.isSuccess && !legal.isFetching ? legal.data : null; const branchRows = branches.isSuccess && !branches.isFetching ? branches.data : null
  const atLimit = Boolean(licenseData && branchOffset === 0 && branchRows && branchRows.length >= licenseData.maxBranches)
  const selected = entities?.find(row => row.id === entityId)
  const candidate = write.pending === 'legal' ? entities?.find(row => row.id === (write.createdId || candidateId)) : branchRows?.find(row => row.id === (write.createdId || candidateId))
  const locked = write.locked || !http || !licenseData || !['trial', 'active', 'grace'].includes(licenseData.status)
  return <div className="operations"><PageHeader title="Configuración" description="Licencia, entidades legales y sucursales · Owner. El servidor decide límites y autorización." />
    <nav><Link to={w.base + '/auditoria'}>Historial de auditoría</Link> · <Link to={w.base + '/importacion'}>Importar productos Excel</Link></nav>
    {!http && <Notice tone="warning">Se requiere conexión HTTPS real.</Notice>}
    <Panel><div className="operation-body"><h2>Licencia actual</h2>{license.isFetching && <p role="status">Consultando licencia…</p>}{license.isError && <ApiErrorState error={license.error} retry={() => void license.refetch()} />}{licenseData && <dl><dt>Estado</dt><dd>{licenseData.status}</dd><dt>Inicio UTC</dt><dd>{licenseData.startsAt}</dd><dt>Vencimiento UTC</dt><dd>{licenseData.expiresAt}</dd><dt>Máximo de sucursales</dt><dd>{licenseData.maxBranches}</dd></dl>}<Button onClick={() => void refresh()} disabled={write.busy}>Actualizar configuración</Button></div></Panel>
    {write.error != null && <ApiErrorState error={write.error} />}{write.notice && <p role="status">{write.notice}</p>}
    {write.pending && <Panel><div className="operation-body"><h2>Alta pendiente de conciliación</h2><p>No repitas la solicitud. Reconsulta los directorios: una fila ausente o un nombre coincidente no demuestran el resultado. No hay idempotencia por nombre o RUC.</p>
      {!write.createdId && <label>Registro original a revisar<select aria-label="Registro original a revisar" value={candidateId} onChange={e => { setCandidateId(e.target.value); setReconciled(false) }}><option value="">Selecciona tras verificar la operación original</option>{write.pending === 'legal' ? entities?.map(row => <option key={row.id} value={row.id}>{row.legalName} · {row.ruc}</option>) : branchRows?.map(row => <option key={row.id} value={row.id}>{row.name} · {row.legalEntityId}</option>)}</select></label>}
      {candidate && <><p>Registro reconsultado: {'legalName' in candidate ? candidate.legalName + ' · ' + candidate.ruc : candidate.name + ' · entidad ' + candidate.legalEntityId} · UTC {candidate.createdAt}</p><label><input type="checkbox" checked={reconciled} onChange={e => setReconciled(e.target.checked)} /> Verifiqué inequívocamente identidad, atributos y finalización de la solicitud original; no hay otra solicitud en curso.</label><Button disabled={!reconciled || write.busy} onClick={() => { write.reconcile(); setReconciled(false); setCandidateId(''); setLegalConfirm(false); setBranchConfirm(false) }}>Continuar tras conciliación</Button></>}
    </div></Panel>}
    <Panel><div className="operation-body"><h2>Entidades legales</h2>{legal.isFetching && <p role="status">Consultando entidades…</p>}{legal.isError && <ApiErrorState error={legal.error} retry={() => void legal.refetch()} />}{entities && <>{!entities.length && <p>No hay entidades en esta página.</p>}<ul>{entities.map(row => <li key={row.id}>{row.legalName} · RUC {row.ruc}</li>)}</ul></>}<PageControls offset={legalOffset} count={entities?.length} busy={write.busy || legal.isFetching} change={offset => { setLegalOffset(offset); setEntityId(''); setBranchConfirm(false); setReconciled(false) }} refresh={() => void legal.refetch()} />
      <form onSubmit={e => { e.preventDefault(); if (legalConfirm && !locked) { setLegalConfirm(false); void write.run({ legalName: legalName.trim(), ruc: ruc.trim() }) } }}><h3>Crear entidad legal</h3><TextField label="Razón social" required value={legalName} disabled={locked} onChange={e => { setLegalName(e.target.value); setLegalConfirm(false) }} /><TextField label="RUC de la entidad" required value={ruc} disabled={locked} onChange={e => { setRuc(e.target.value); setLegalConfirm(false) }} /><p>Usa datos legales verdaderos y verificados. No generes identificadores fiscales ficticios.</p><label><input type="checkbox" checked={legalConfirm} disabled={locked} onChange={e => setLegalConfirm(e.target.checked)} /> Confirmo la creación persistente de {legalName || 'esta entidad'} con el RUC indicado.</label><Button type="submit" disabled={locked || !legalConfirm || !legalName.trim() || !ruc.trim()}>Crear entidad legal</Button></form>
    </div></Panel>
    <Panel><div className="operation-body"><h2>Sucursales y su entidad legal</h2>{branches.isFetching && <p role="status">Consultando sucursales…</p>}{branches.isError && <ApiErrorState error={branches.error} retry={() => void branches.refetch()} />}{branchRows && <>{!branchRows.length && <p>No hay sucursales en esta página.</p>}<ul>{branchRows.map(row => <li key={row.id}>{row.name} · Entidad: {entities?.find(entity => entity.id === row.legalEntityId)?.legalName ?? row.legalEntityId}{row.isMainHub ? ' · Sede principal' : ''}</li>)}</ul></>}<PageControls offset={branchOffset} count={branchRows?.length} busy={write.busy || branches.isFetching} change={offset => { setBranchOffset(offset); setReconciled(false) }} refresh={() => void branches.refetch()} />
      {atLimit && <Notice tone="warning">Límite de sucursales de la licencia alcanzado. El servidor vuelve a comprobarlo dentro de la creación.</Notice>}
      <form onSubmit={e => { e.preventDefault(); if (branchConfirm && selected && !locked && !atLimit) { setBranchConfirm(false); void write.run({ legalEntityId: selected.id, name: name.trim() }) } }}><h3>Crear sucursal</h3><TextField label="Nombre de la nueva sucursal" value={name} required disabled={locked || atLimit} onChange={e => { setName(e.target.value); setBranchConfirm(false) }} /><label>Entidad legal existente<select aria-label="Entidad legal existente" value={entityId} disabled={locked || atLimit || !entities} onChange={e => { setEntityId(e.target.value); setBranchConfirm(false) }}><option value="">Selecciona en la página de entidades consultada</option>{entities?.map(row => <option key={row.id} value={row.id}>{row.legalName} · {row.ruc}</option>)}</select></label><label><input type="checkbox" checked={branchConfirm} disabled={locked || atLimit} onChange={e => setBranchConfirm(e.target.checked)} /> Confirmo crear {name || 'esta sucursal'} para {selected?.legalName ?? 'la entidad seleccionada'}.</label><Button type="submit" disabled={locked || atLimit || !branchConfirm || !selected || !name.trim()}>Crear sucursal</Button></form>
    </div></Panel></div>
}
