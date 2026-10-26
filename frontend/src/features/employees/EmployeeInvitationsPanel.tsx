import { useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { ApiErrorState, isUuid, roleLabels, useRuntime, useWorkspace } from '../access'
import type { TenantRole } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { Button } from '../../shared/ui/Button'
import { Notice } from '../../shared/ui/Notice'
import { Panel } from '../../shared/ui/Panel'
import { TextField } from '../../shared/ui/TextField'
import { PageControls } from '../../shared/ui/PageControls'
import { Drawer, Modal } from '../../shared/ui/Modal'
import { DateText } from '../../shared/ui/DateText'
import { Badge } from '../../shared/ui/Badge'
import { Steps } from '../../shared/ui/Steps'
import { invitationsApi } from './invitations.api'
import type { EmployeeInvitation } from './invitations.api'

const statusLabels = { pending_acceptance: 'Esperando aceptación', accepted_pending_approval: 'Cuenta aceptante pendiente de aprobación', approved: 'Membresía creada', revoked: 'Revocada', expired: 'Vencida' }
export function EmployeeInvitationsPanel({ createOpen, onCreateOpen, onCreateClose }: { createOpen?: boolean; onCreateOpen?: () => void; onCreateClose?: () => void } = {}) {
  const w = useWorkspace(); const { http } = useRuntime(); const client = useQueryClient(); const [params, setParams] = useSearchParams()
  const [offset, setOffset] = useState(0); const [role, setRole] = useState<TenantRole>('cashier'); const [reviewed, setReviewed] = useState(false)
  const [busy, setBusy] = useState(false); const running = useRef(false); const [error, setError] = useState<unknown>(null)
  const [notice, setNotice] = useState(''); const [link, setLink] = useState(''); const [recovered, setRecovered] = useState<EmployeeInvitation | null>(null)
  const controller = useRef<AbortController | null>(null); const mounted = useRef(true)
  const [ownOpen, setOwnOpen] = useState(false); const [directoryOpen, setDirectoryOpen] = useState(false)
  const opening = createOpen ?? ownOpen
  function hideCreate() { setOwnOpen(false); onCreateClose?.() }
  function closeCreate() { hideCreate(); setRole('cashier'); setReviewed(false) }
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; controller.current?.abort() } }, [])
  const requestId = params.get('invitationRequest'); const actionId = params.get('invitationTarget'); const pending = Boolean(requestId || actionId)
  const api = () => { if (!http) throw new ApiError(0, 'client.https_required'); return invitationsApi(http) }
  const listKey = scopedKey(w, 'employee-invitations', w.session.userId, w.access.membershipId)
  const list = useQuery({ queryKey: [...listKey, offset], queryFn: ({ signal }) => api().list(w, offset, undefined, signal), enabled: Boolean(http), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  function marker(kind: 'create' | 'approve' | 'revoke' | null, id?: string) {
    setParams(current => {
      const next = new URLSearchParams(current)
      for (const key of ['invitationRequest', 'invitationTarget', 'invitationAction']) next.delete(key)
      if (kind && id) { next.set(kind === 'create' ? 'invitationRequest' : 'invitationTarget', id); next.set('invitationAction', kind) }
      return next
    }, { replace: true })
  }
  async function refresh() {
    await client.invalidateQueries({ queryKey: listKey })
    await client.invalidateQueries({ queryKey: scopedKey(w, 'employees') })
  }
  async function create() {
    if (!reviewed || busy || running.current || pending || !http || w.access.role !== 'owner') return
    const intention = crypto.randomUUID()
    running.current = true; setBusy(true); setError(null); setNotice(''); setLink(''); setRecovered(null); marker('create', intention)
    controller.current = new AbortController()
    try {
      const result = await api().create(w, intention, role, controller.current.signal)
      if (!mounted.current) return
      setLink(result.ticket ? `${window.location.origin}/aceptar-invitacion#${result.ticket}` : '')
      setNotice(result.ticket ? 'Invitación creada. Copia el enlace ahora; no se conserva ni se puede recuperar después.' : 'Intención recuperada sin reemitir el secreto. Si perdiste el enlace, revoca la invitación antes de emitir otra.')
      marker(null); setReviewed(false); await refresh()
    } catch (failure) {
      if (!mounted.current) return
      setError(failure)
      if (failure instanceof ApiError && !failure.outcomeUnknown && failure.status !== 409) marker(null)
    } finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  async function transition(value: EmployeeInvitation, kind: 'approve' | 'revoke', reason = '') {
    if (busy || running.current || pending || !http || w.access.role !== 'owner') return
    running.current = true; setBusy(true); setError(null); setNotice(''); setRecovered(null); marker(kind, value.id); controller.current = new AbortController()
    try {
      const result = kind === 'approve' ? await api().approve(w, value, controller.current.signal) : await api().revoke(w, value, reason, controller.current.signal)
      if (!mounted.current) return
      setNotice(kind === 'approve' ? 'Membresía creada por aprobación. Asigna sucursales y horario al trabajador desde su detalle.' : 'Invitación revocada; no se creó una membresía.')
      setRecovered(result); marker(null); await refresh()
    } catch (failure) {
      if (!mounted.current) return
      setError(failure)
      if (failure instanceof ApiError && !failure.outcomeUnknown) marker(null)
    } finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  async function reconcile() {
    if (running.current || !http) return
    running.current = true; setBusy(true); setError(null); controller.current = new AbortController()
    try {
      const result = requestId && isUuid(requestId) ? (await api().list(w, 0, requestId, controller.current.signal))[0] : actionId && isUuid(actionId) ? await api().read(w, actionId, controller.current.signal) : undefined
      if (!mounted.current) return
      if (!result) { setNotice('La lectura aún no encuentra la intención. Esto no prueba rollback: no se enviará otra creación.'); return }
      setRecovered(result)
      if (requestId || result.status === 'approved' || result.status === 'revoked') {
        marker(null); setNotice(requestId ? 'Intención original localizada. El enlace no se reemite: revócala si no pudiste conservarlo.' : 'Estado terminal conciliado sin repetir la escritura. Comprueba si corresponde a la acción original o a otro cambio concurrente.'); await refresh()
      } else setNotice('La operación no tiene todavía resultado terminal. No se reenviará automáticamente.')
    } catch (failure) { if (mounted.current) setError(failure) }
    finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  return <Panel><div className="operation-body"><div className="operation-actions"><h2>Invitaciones por enlace</h2>
    <Button variant="secondary" onClick={() => setDirectoryOpen(!directoryOpen)}>{directoryOpen ? 'Ocultar invitaciones' : 'Consultar invitaciones'}</Button>
    {createOpen === undefined && <Button variant="quiet" disabled={busy || pending || !http} onClick={() => { if (onCreateOpen) onCreateOpen(); else setOwnOpen(true) }}>Crear enlace de invitación</Button>}</div>
    <details className="inline-help"><summary>Invitación por enlace · 15 minutos</summary><p>La cuenta Google acepta y el propietario emisor verifica y aprueba. No enviamos correos automáticamente.</p></details>
    {error != null && <ApiErrorState error={error} title="No pudimos completar la invitación" />}
    {notice && <p role="status">{notice}</p>}
    {pending && <Notice tone="warning">Hay una operación pendiente. No crees otra intención ni repitas la aprobación sin consultar la original. <Button variant="secondary" disabled={busy} onClick={() => void reconcile()}>Consultar invitación original</Button></Notice>}
    {requestId && !opening && <Button data-recovery-focus variant="secondary" disabled={busy} onClick={() => { if (onCreateOpen) onCreateOpen(); else setOwnOpen(true) }}>Revisar invitación pendiente sin reenviar</Button>}
    <Modal open={opening} title="Nuevo integrante por invitación" description="Crear enlace → aceptación con Google → revisión y aprobación. Compartir el enlace no concede permisos." dirty={reviewed || role !== 'cashier'} busy={busy} recovery={pending} onClose={closeCreate} onRecoveryClose={hideCreate}>
      <Steps labels={['Elegir rol', 'Compartir enlace', 'Revisar aceptación']} current={link ? 1 : 0} label="Incorporación por enlace" focusOnChange />
      {error != null && <ApiErrorState error={error} title="No pudimos completar la invitación" />}
      {notice && <p role="status">{notice}</p>}
      {pending && <Notice tone="warning">Solicitud pendiente. No emitas otra intención.<Button disabled={busy} onClick={() => void reconcile()}>Consultar invitación original</Button></Notice>}
    <form className="operation-body" aria-label="Crear invitación de empleado" onSubmit={event => { event.preventDefault(); void create() }}>
      <div className="workflow-context"><strong>1. Define el rol · 2. Comparte el enlace · 3. Verifica y aprueba</strong><p>La cuenta destinataria ingresa con Google. La aceptación queda pendiente hasta tu aprobación independiente; después configura sucursales y horario.</p></div>
      <label>Rol de la invitación<select aria-label="Rol de la invitación" value={role} disabled={busy || pending} onChange={event => { setRole(event.target.value as TenantRole); setReviewed(false) }}><option value="cashier">Cajero</option><option value="pharmacist">Químico farmacéutico</option><option value="owner">Propietario</option></select></label>
      <label><input type="checkbox" checked={reviewed} disabled={busy || pending} onChange={event => setReviewed(event.target.checked)} /> Revisé el rol; verificaré por un canal independiente la cuenta que acepte antes de incorporarla.</label>
      <Button type="submit" disabled={!http || busy || pending || !reviewed}>Crear enlace temporal</Button>
    </form>
    {link && <section className="invitation-link-panel"><Notice tone="warning">Copia el enlace ahora. Solo está disponible en esta sesión de la página y no puede recuperarse después.</Notice><TextField label="Enlace temporal para compartir" value={link} readOnly /><Button variant="secondary" onClick={() => { void navigator.clipboard.writeText(link).then(() => { if (mounted.current) setNotice('Enlace copiado.') }).catch(() => { if (mounted.current) setNotice('Selecciona y copia el enlace visible; no se enviará automáticamente.') }) }}>Copiar enlace</Button></section>}
    </Modal>
    {link && <section aria-label="Enlace temporal de invitación"><TextField label="Enlace temporal (solo se muestra ahora)" value={link} readOnly />
      <p>Compártelo por un canal seguro. El trabajador debe iniciar Google y volver a abrir este enlace; poseerlo no concede permisos.</p>
      <Button variant="secondary" onClick={() => { void navigator.clipboard.writeText(link).then(() => { if (mounted.current) setNotice('Enlace copiado por solicitud del operador.') }).catch(() => { if (mounted.current) setNotice('No se pudo copiar. Selecciona el enlace visible; no se enviará automáticamente.') }) }}>Copiar enlace temporal</Button>
      <Button variant="quiet" onClick={() => setLink('')}>Ocultar enlace</Button></section>}
    {recovered && <p>Invitación consultada: {statusLabels[recovered.status]} · {roleLabels[recovered.role]}. El estado no sustituye la revisión de identidad.</p>}
    <div hidden={!directoryOpen && !pending && !recovered}>
    {list.isFetching && <p role="status">Consultando invitaciones…</p>}
    {list.isError && <ApiErrorState error={list.error} title="No pudimos consultar invitaciones" />}
    {list.data && !list.isError && <>{!list.data.length && <p>No hay invitaciones en esta página.</p>}{list.data.map(value => <InvitationCard key={`${value.id}/${value.version}`} value={value} disabled={busy || pending || !http} busy={busy} pending={pending} error={error} notice={notice} reconcile={() => void reconcile()} transition={transition} />)}</>}
    <PageControls offset={offset} count={list.data?.length} busy={busy || list.isFetching || pending} change={setOffset} refresh={() => void list.refetch()} />
    </div>
  </div></Panel>
}
function InvitationCard({ value, disabled, transition, busy, pending, error, notice, reconcile }: { value: EmployeeInvitation; disabled: boolean; busy: boolean; pending: boolean; error: unknown; notice: string; reconcile: () => void; transition: (value: EmployeeInvitation, kind: 'approve' | 'revoke', reason?: string) => Promise<void> }) {
  const [reviewed, setReviewed] = useState(false); const [reason, setReason] = useState('')
  const [opened, setOpened] = useState(false)
  return <section className="invitation-summary" aria-label={`Invitación ${roleLabels[value.role]} ${statusLabels[value.status]}`}><div className="operation-actions"><h3>{roleLabels[value.role]}</h3><Badge tone={value.status === 'approved' ? 'success' : 'neutral'}>{statusLabels[value.status]}</Badge><Button variant="quiet" onClick={() => setOpened(true)}>Revisar invitación</Button></div><p>Vence: <DateText value={value.expiresAt} /></p>
    <Drawer open={opened} title={`Invitación · ${roleLabels[value.role]}`} onClose={() => { setOpened(false); setReviewed(false); setReason('') }} onRecoveryClose={() => setOpened(false)} dirty={Boolean(reviewed || reason)} busy={busy} recovery={pending}>
    <div className="operation-body"><p>{statusLabels[value.status]} · Vence <DateText value={value.expiresAt} />. El vencimiento de una invitación ya aprobada no desactiva su membresía.</p>
    {error != null && <ApiErrorState error={error} />}{notice && <p role="status">{notice}</p>}
    {pending && <Notice tone="warning">Operación pendiente de verificar.<Button disabled={busy} onClick={reconcile}>Consultar invitación original</Button></Notice>}
    {value.acceptedAt && <p>Cuenta que aceptó: {value.acceptedDisplayName} · {value.acceptedEmail}. Correo/nombre son datos de revisión, no autoridad ni prueba de pertenencia.</p>}
    {value.canApprove && <><label><input type="checkbox" disabled={disabled} checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Verifiqué de forma independiente esta cuenta y confirmo su incorporación con el rol indicado.</label>
      <Button disabled={disabled || !reviewed} onClick={() => void transition(value, 'approve')}>Aprobar incorporación</Button></>}
    {!['approved', 'revoked'].includes(value.status) && <><TextField label="Motivo para revocar invitación" value={reason} disabled={disabled} maxLength={256} onChange={event => setReason(event.target.value)} />
      <Button variant="secondary" disabled={disabled || !reason.trim()} onClick={() => void transition(value, 'revoke', reason)}>Revocar invitación</Button></>}
    </div></Drawer>
  </section>
}
