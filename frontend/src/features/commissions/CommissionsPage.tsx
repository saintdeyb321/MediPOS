import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { ProductLabel, ProductLookup } from '../catalog'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { decimalLabel } from '../../shared/api/decimal'
import { Button } from '../../shared/ui/Button'
import { PageHeader } from '../../shared/ui/PageHeader'
import { PageControls } from '../../shared/ui/PageControls'
import { Notice } from '../../shared/ui/Notice'
import { Panel } from '../../shared/ui/Panel'
import { commissionsApi } from './api'
import { useCommissionWrite } from './useCommissionWrite'
import { RuleForm } from './RuleForm'
import '../../shared/styles/operations.css'

export function CommissionsPage() {
  const w = useWorkspace()
  if (w.access.role !== 'owner') return <Notice tone="warning">Configurar comisiones requiere Owner con acceso vigente.</Notice>
  return <CommissionsWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function CommissionsWorkspace() {
  const w = useWorkspace(); const { http } = useRuntime(); const api = useMemo(() => http ? commissionsApi(http) : null, [http]); const write = useCommissionWrite()
  const [product, setProduct] = useState(''); const [productName, setProductName] = useState(''); const [inactive, setInactive] = useState(true); const [offset, setOffset] = useState(0); const [reviewSetting, setReviewSetting] = useState<string | null>(null); const [deactivate, setDeactivate] = useState(''); const [reconciled, setReconciled] = useState(false); const [recoveryOffset, setRecoveryOffset] = useState(0)
  const configuration = useQuery({ queryKey: scopedKey(w, 'commissions-settings', w.session.userId, w.access.membershipId), queryFn: ({ signal }) => { if (!api) throw new ApiError(0, 'client.https_required'); return api.settings(w, signal) }, enabled: Boolean(api), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const rules = useQuery({ queryKey: scopedKey(w, 'commission-rules', w.session.userId, w.access.membershipId, product, inactive, offset), queryFn: ({ signal }) => { if (!api) throw new ApiError(0, 'client.https_required'); return api.rules(w, product || undefined, inactive, offset, 20, signal) }, enabled: Boolean(api), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const signature = configuration.data ? JSON.stringify(configuration.data) : ''
  return <div className="operations"><PageHeader title="Comisiones" description={`${w.tenantName} · configuración Owner. No registra ventas ni recalcula devengamientos existentes.`} /><Link to={w.base + '/reportes'}>Consultar reportes y comisiones devengadas</Link>
    {write.error != null && <ApiErrorState error={write.error} title="No pudimos completar el cambio de comisión" />}{write.notice && <p role="status">{write.notice}</p>}
    {write.pending && <Panel><div className="operation-body"><h2>Cambio pendiente de conciliación</h2><p>Se conserva en la dirección al recargar. No repitas PUT/POST. Una configuración o regla con valores coincidentes no demuestra que terminó la escritura original. Un cambio concurrente puede desactivar una regla reconocida por su ID; no se sustituye por la más nueva.</p>
      <Button disabled={write.busy} onClick={() => { setReconciled(false); void write.consult(recoveryOffset) }}>Consultar operación de comisión original</Button>
      {write.observed?.settings && <p>Configuración consultada: {write.observed.settings.isEnabled ? 'Habilitada' : 'Deshabilitada'} · UTC {write.observed.settings.updatedAt ?? 'Sin configurar'}</p>}
      {write.observed?.rules && <><ul>{write.observed.rules.map(rule => <li key={rule.id}>{rule.ruleType === 'fixed' ? 'Fijo PEN/base' : 'Porcentaje'} {decimalLabel(rule.value)} · inicio UTC {rule.validFrom} · creada UTC {rule.createdAt} · {rule.isActive ? 'Activa' : 'Inactiva'}</li>)}</ul><PageControls size={100} offset={recoveryOffset} count={write.observed.rules.length} busy={write.busy} change={value => { setRecoveryOffset(value); setReconciled(false); void write.consult(value) }} refresh={() => { setReconciled(false); void write.consult(recoveryOffset) }} /></>}
      {write.observed && write.pending !== 'deactivate' && <><label><input type="checkbox" checked={reconciled} onChange={event => setReconciled(event.target.checked)} /> Concilié inequívocamente la operación original y su finalización; no basta una coincidencia de valores y no queda escritura en curso.</label><Button disabled={!reconciled || write.busy} onClick={() => { write.acknowledge(); setReconciled(false) }}>Continuar tras conciliación verificada de comisión</Button></>}
      {write.pending === 'deactivate' && <p>Una desactivación incierta solo se concilia al observar inactiva la misma revisión original; no basta otra regla ni una lectura todavía activa.</p>}
    </div></Panel>}
    <Panel><div className="operation-body"><h2>Habilitación del negocio</h2>{configuration.isFetching && <p role="status">Consultando configuración…</p>}{configuration.isError && <ApiErrorState error={configuration.error} title="No pudimos consultar configuración de comisiones" />}
      {configuration.data && !configuration.isError && <><p>{configuration.data.isConfigured ? 'Configuración registrada' : 'Sin configurar: consultar no crea configuración'} · {configuration.data.isEnabled ? 'Comisiones habilitadas' : 'Comisiones deshabilitadas'} · Último cambio UTC {configuration.data.updatedAt ?? 'No existe'}</p><fieldset disabled={write.locked || configuration.isFetching}><legend>Cambiar habilitación</legend><label><input type="checkbox" checked={reviewSetting === signature} onChange={event => setReviewSetting(event.target.checked ? signature : null)} /> Revisé el estado actual y autorizo {configuration.data.isEnabled ? 'deshabilitar' : 'habilitar'} las comisiones del negocio.</label><Button disabled={reviewSetting !== signature} onClick={() => { if (configuration.data) { setReviewSetting(null); void write.run('settings', !configuration.data.isEnabled) } }}>Confirmar {configuration.data.isEnabled ? 'deshabilitación' : 'habilitación'} de comisiones</Button></fieldset></>}
      <Button variant="quiet" disabled={write.busy || configuration.isFetching} onClick={() => void configuration.refetch()}>Actualizar configuración</Button>
    </div></Panel>
    <Panel><div className="operation-body"><h2>Reglas y revisiones</h2><ProductLookup disabled={write.locked} onSelect={value => { setProduct(value.product.id); setProductName(value.product.name); setOffset(0) }} /><p>{product ? 'Producto filtrado: ' + productName : 'Todos los productos autorizados'}</p><Button variant="quiet" disabled={write.locked || !product} onClick={() => { setProduct(''); setProductName(''); setOffset(0) }}>Quitar filtro de reglas</Button><label><input type="checkbox" checked={inactive} disabled={write.locked} onChange={event => { setInactive(event.target.checked); setOffset(0) }} /> Incluir reglas inactivas e historial</label>
      {rules.isFetching && <p role="status">Consultando reglas…</p>}{rules.isError && <ApiErrorState error={rules.error} title="No pudimos consultar reglas" />}
      {rules.data && !rules.isError && <div className="operation-table"><table><caption>Reglas consultadas del servidor</caption><thead><tr><th>Producto</th><th>Tipo / valor</th><th>Vigencia UTC</th><th>Estado</th><th>Creación UTC</th><th>Acción</th></tr></thead><tbody>{rules.data.map(rule => <tr key={rule.id}><td><ProductLabel id={rule.businessProductId} /></td><td>{rule.ruleType === 'fixed' ? 'S/ por unidad base' : '% del neto de línea'} {decimalLabel(rule.value)}</td><td>{rule.validFrom} → {rule.validUntil ?? 'Sin fin'}</td><td>{rule.isActive ? 'Activa (revisar vigencia)' : 'Inactiva'}</td><td>{rule.createdAt}</td><td>{rule.isActive ? <fieldset disabled={write.locked}><legend>Desactivar esta revisión</legend><label><input type="checkbox" checked={deactivate === rule.id} onChange={event => setDeactivate(event.target.checked ? rule.id : '')} /> Confirmo desactivar esta revisión conservando su historial.</label><Button variant="secondary" disabled={deactivate !== rule.id} onClick={() => { setDeactivate(''); void write.run('deactivate', rule) }}>Desactivar esta regla</Button></fieldset> : 'Historial conservado'}</td></tr>)}</tbody></table>{!rules.data.length && <p>No hay reglas en esta página. No se crearon reglas ni devengamientos.</p>}</div>}
      <PageControls offset={offset} count={rules.isError ? undefined : rules.data?.length} busy={write.busy || rules.isFetching} change={setOffset} refresh={() => void rules.refetch()} />
    </div></Panel>
    <Panel><div className="operation-body"><RuleForm locked={write.locked || !api} save={(productId, rule) => void write.run('rule', { productId, rule })} /></div></Panel>
  </div>
}
