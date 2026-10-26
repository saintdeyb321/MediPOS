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
import { Modal } from '../../shared/ui/Modal'
import { DateText } from '../../shared/ui/DateText'
import type { Rule } from './api'
import { commissionsApi } from './api'
import { useCommissionWrite } from './useCommissionWrite'
import { RuleForm } from './RuleForm'
import { CommissionRecovery } from './CommissionRecovery'
import '../../shared/styles/operations.css'

export function CommissionsPage() {
  const w = useWorkspace()
  if (w.access.role !== 'owner') return <Notice tone="warning">Configurar comisiones requiere Owner con acceso vigente.</Notice>
  return <CommissionsWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function CommissionsWorkspace() {
  const w = useWorkspace(); const { http } = useRuntime(); const api = useMemo(() => http ? commissionsApi(http) : null, [http]); const write = useCommissionWrite()
  const [product, setProduct] = useState(''); const [productName, setProductName] = useState(''); const [inactive, setInactive] = useState(true); const [offset, setOffset] = useState(0); const [reviewSetting, setReviewSetting] = useState<string | null>(null); const [deactivate, setDeactivate] = useState(''); const [reconciled, setReconciled] = useState(false); const [recoveryOffset, setRecoveryOffset] = useState(0)
  const [ruleOpen, setRuleOpen] = useState(false); const [settingsOpen, setSettingsOpen] = useState(false); const [deactivation, setDeactivation] = useState<Rule | null>(null); const [dirty, setDirty] = useState(false)
  const [ruleHidden, setRuleHidden] = useState(false); const [deactivationHidden, setDeactivationHidden] = useState(false)
  const configuration = useQuery({ queryKey: scopedKey(w, 'commissions-settings', w.session.userId, w.access.membershipId), queryFn: ({ signal }) => { if (!api) throw new ApiError(0, 'client.https_required'); return api.settings(w, signal) }, enabled: Boolean(api), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const rules = useQuery({ queryKey: scopedKey(w, 'commission-rules', w.session.userId, w.access.membershipId, product, inactive, offset), queryFn: ({ signal }) => { if (!api) throw new ApiError(0, 'client.https_required'); return api.rules(w, product || undefined, inactive, offset, 20, signal) }, enabled: Boolean(api), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const signature = configuration.data ? JSON.stringify(configuration.data) : ''
  return <div className="operations"><PageHeader title="Comisiones" description={`Reglas y revisiones de ${w.tenantName}. El historial ya registrado se conserva.`} action={<Button disabled={write.locked || !api} onClick={() => { setDirty(false); setRuleHidden(false); setRuleOpen(true) }}>Nueva regla</Button>} /><Link className="text-link" to={w.base + '/reportes'}>Consultar comisiones devengadas en Reportes</Link>
    {write.error != null && <ApiErrorState error={write.error} title="No pudimos completar el cambio de comisión" />}{write.notice && <p role="status">{write.notice}</p>}
    {write.pending && <Panel><div className="operation-body"><h2>Cambio pendiente de conciliación</h2><p>Se conserva en la dirección al recargar. No repitas PUT/POST. Una configuración o regla con valores coincidentes no demuestra que terminó la escritura original. Un cambio concurrente puede desactivar una regla reconocida por su ID; no se sustituye por la más nueva.</p>
      <Button disabled={write.busy} onClick={() => { setReconciled(false); void write.consult(recoveryOffset) }}>Consultar operación de comisión original</Button>
      <Button data-recovery-focus variant="secondary" disabled={write.busy || (write.pending === 'deactivate' && !deactivation) || (write.pending === 'rule' && !ruleOpen)} onClick={() => { if (write.pending === 'settings') setSettingsOpen(true); else if (write.pending === 'deactivate') setDeactivationHidden(false); else setRuleHidden(false) }}>Revisar solicitud pendiente sin reenviar</Button>
      {write.observed?.settings && <p>Configuración consultada: {write.observed.settings.isEnabled ? 'Habilitada' : 'Deshabilitada'} · UTC {write.observed.settings.updatedAt ?? 'Sin configurar'}</p>}
      {write.observed?.rules && <><ul>{write.observed.rules.map(rule => <li key={rule.id}>{rule.ruleType === 'fixed' ? 'Fijo PEN/base' : 'Porcentaje'} {decimalLabel(rule.value)} · inicio UTC {rule.validFrom} · creada UTC {rule.createdAt} · {rule.isActive ? 'Activa' : 'Inactiva'}</li>)}</ul><PageControls size={100} offset={recoveryOffset} count={write.observed.rules.length} busy={write.busy} change={value => { setRecoveryOffset(value); setReconciled(false); void write.consult(value) }} refresh={() => { setReconciled(false); void write.consult(recoveryOffset) }} /></>}
      {write.observed && write.pending !== 'deactivate' && <><label><input type="checkbox" checked={reconciled} onChange={event => setReconciled(event.target.checked)} /> Concilié inequívocamente la operación original y su finalización; no basta una coincidencia de valores y no queda escritura en curso.</label><Button disabled={!reconciled || write.busy} onClick={() => { write.acknowledge(); setReconciled(false) }}>Continuar tras conciliación verificada de comisión</Button></>}
      {write.pending === 'deactivate' && <p>Una desactivación incierta solo se concilia al observar inactiva la misma revisión original; no basta otra regla ni una lectura todavía activa.</p>}
    </div></Panel>}
    <Panel><div className="operation-body"><h2>Habilitación del negocio</h2>{configuration.isFetching && <p role="status">Consultando configuración…</p>}{configuration.isError && <ApiErrorState error={configuration.error} title="No pudimos consultar configuración de comisiones" />}
      {configuration.data && (!configuration.isError || settingsOpen) && <><p>{configuration.data.isConfigured ? 'Configuración registrada' : 'Sin configurar'} · {configuration.data.isEnabled ? 'Comisiones habilitadas' : 'Comisiones deshabilitadas'} · <DateText value={configuration.data.updatedAt} /></p><Button variant="secondary" disabled={write.locked || configuration.isFetching || configuration.isError} onClick={() => setSettingsOpen(true)}>Cambiar habilitación</Button>
        <Modal open={settingsOpen} title="Habilitación de comisiones" onClose={() => { setSettingsOpen(false); setReviewSetting(null) }} onRecoveryClose={() => setSettingsOpen(false)} dirty={reviewSetting !== null} busy={write.busy} recovery={Boolean(write.pending)}>
          {write.error != null && <ApiErrorState error={write.error} />}<CommissionRecovery write={write} />{write.notice && <p role="status">{write.notice}</p>}
          {configuration.isError && <ApiErrorState error={configuration.error} retry={() => void configuration.refetch()} />}
          <fieldset disabled={write.locked || configuration.isFetching || configuration.isError}><legend>Cambiar habilitación</legend><label><input type="checkbox" checked={reviewSetting === signature} onChange={event => setReviewSetting(event.target.checked ? signature : null)} /> Revisé el estado actual y autorizo {configuration.data.isEnabled ? 'deshabilitar' : 'habilitar'} las comisiones del negocio.</label><Button disabled={reviewSetting !== signature} onClick={() => { if (configuration.data) { setReviewSetting(null); void write.run('settings', !configuration.data.isEnabled) } }}>Confirmar {configuration.data.isEnabled ? 'deshabilitación' : 'habilitación'} de comisiones</Button></fieldset>
        </Modal></>}
      <Button variant="quiet" disabled={write.busy || configuration.isFetching} onClick={() => void configuration.refetch()}>Actualizar configuración</Button>
    </div></Panel>
    <Panel><div className="operation-body"><h2>Reglas y revisiones</h2><details className="inline-help"><summary>Filtrar reglas por producto</summary><ProductLookup disabled={write.locked} onSelect={value => { setProduct(value.product.id); setProductName(value.product.name); setOffset(0) }} /></details><p>{product ? 'Producto filtrado: ' + productName : 'Todos los productos autorizados'}</p><Button variant="quiet" disabled={write.locked || !product} onClick={() => { setProduct(''); setProductName(''); setOffset(0) }}>Quitar filtro de reglas</Button><label><input type="checkbox" checked={inactive} disabled={write.locked} onChange={event => { setInactive(event.target.checked); setOffset(0) }} /> Incluir reglas inactivas e historial</label>
      {rules.isFetching && <p role="status">Consultando reglas…</p>}{rules.isError && <ApiErrorState error={rules.error} title="No pudimos consultar reglas" />}
      {rules.data && !rules.isError && <div className="operation-table"><table><caption>Reglas consultadas del servidor</caption><thead><tr><th>Producto</th><th>Tipo / valor</th><th>Vigencia · Lima</th><th>Estado</th><th>Creación</th><th>Acción</th></tr></thead><tbody>{rules.data.map(rule => <tr key={rule.id}><td><ProductLabel id={rule.businessProductId} /></td><td>{rule.ruleType === 'fixed' ? 'S/ por unidad base' : '% del neto de línea'} {decimalLabel(rule.value)}</td><td><DateText value={rule.validFrom} /> → {rule.validUntil ? <DateText value={rule.validUntil} /> : 'Sin fin'}</td><td>{rule.isActive ? 'Activa (revisar vigencia)' : 'Inactiva'}</td><td><DateText value={rule.createdAt} /></td><td>{rule.isActive ? <Button variant="quiet" disabled={write.locked} onClick={() => { setDeactivate(''); setDeactivationHidden(false); setDeactivation(rule) }}>Desactivar revisión</Button> : 'Historial conservado'}</td></tr>)}</tbody></table>{!rules.data.length && <p>No hay reglas en esta página. No se crearon reglas ni devengamientos.</p>}</div>}
      <PageControls offset={offset} count={rules.isError ? undefined : rules.data?.length} busy={write.busy || rules.isFetching} change={setOffset} refresh={() => void rules.refetch()} />
    </div></Panel>
    <Modal open={ruleOpen && !ruleHidden} wide title="Nueva regla de comisión" onClose={() => { setRuleOpen(false); setRuleHidden(false); setDirty(false) }} onRecoveryClose={() => setRuleHidden(true)} dirty={dirty} busy={write.busy} recovery={Boolean(write.pending)}>
      <div className="operation-body" onChangeCapture={() => setDirty(true)}>{write.error != null && <ApiErrorState error={write.error} />}<CommissionRecovery write={write} />{write.notice && <p role="status">{write.notice}</p>}
        {ruleOpen && <RuleForm locked={write.locked || !api} save={(productId, rule) => void write.run('rule', { productId, rule })} />}
      </div>
    </Modal>
    <Modal open={deactivation !== null && !deactivationHidden} title="Desactivar revisión de comisión" onClose={() => { setDeactivation(null); setDeactivationHidden(false); setDeactivate('') }} onRecoveryClose={() => setDeactivationHidden(true)} dirty={Boolean(deactivate)} busy={write.busy} recovery={Boolean(write.pending)}>
      {write.error != null && <ApiErrorState error={write.error} />}<CommissionRecovery write={write} />{write.notice && <p role="status">{write.notice}</p>}
      {deactivation && <div className="operation-body"><p><ProductLabel id={deactivation.businessProductId} /> · {decimalLabel(deactivation.value)} · Inicio <DateText value={deactivation.validFrom} /></p><p>Desactivar conserva esta revisión y los devengamientos registrados.</p><label><input type="checkbox" disabled={write.locked} checked={deactivate === deactivation.id} onChange={event => setDeactivate(event.target.checked ? deactivation.id : '')} /> Confirmo desactivar esta revisión original.</label><Button variant="danger" disabled={write.locked || deactivate !== deactivation.id} onClick={() => { setDeactivate(''); void write.run('deactivate', deactivation) }}>Desactivar esta regla</Button></div>}
    </Modal>
  </div>
}
