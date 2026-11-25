import { PlatformReadError } from './PlatformAdmissionError'
import { Button } from '../../shared/ui/Button'
import { DateText } from '../../shared/ui/DateText'
import { Drawer } from '../../shared/ui/Modal'
import { Notice } from '../../shared/ui/Notice'
import { licenseLabel } from '../../shared/ui/licenseLabel'
import { licenseActions, ownerLabel } from './presentation'
import type { PlatformConsoleState } from './PlatformTenantDirectory'

export function PlatformTenantDetails({ p, open, close, manage }: { p: PlatformConsoleState; open: boolean; close: () => void; manage: (task: 'license' | 'owner') => void }) {
  const detail = p.detail.data
  return <Drawer open={open} title="Ficha de botica" onClose={close} busy={p.busy}>
    <div className="operation-body">
      {p.notice && <Notice><span role="status">{p.notice}</span></Notice>}
      {p.notice && p.list.isError && <p>La lista de boticas no pudo actualizarse. Puedes reconsultarla al volver al directorio.</p>}
      <PlatformReadError error={p.error} failedAt={p.failedAt} fetching={p.busy} />
      {p.detail.isFetching && <p role="status">Consultando licencia y propietario…</p>}
      <PlatformReadError error={p.detail.error} failedAt={p.detail.errorUpdatedAt} fetching={p.detail.isFetching} retry={p.admitted ? () => void p.detail.refetch() : undefined} />
      {detail && <><h3>{detail.tenant.tradingName}</h3>
        {p.detail.isError && <p>Datos de la última lectura o del último cambio confirmado.</p>}
        <dl className="summary-grid"><div><dt>Licencia</dt><dd>{licenseLabel(detail.license.status)}</dd></div><div><dt>Inicio</dt><dd><DateText value={detail.license.startsAt} /></dd></div><div><dt>Vencimiento</dt><dd><DateText value={detail.license.expiresAt} /></dd></div><div><dt>Máximo de sucursales</dt><dd>{detail.license.maxBranches}</dd></div></dl>
        <h3>Primer propietario</h3>
        {p.initialOwner.isFetching && <p role="status">Consultando propietario…</p>}
        <PlatformReadError error={p.initialOwner.error} failedAt={p.initialOwner.errorUpdatedAt} fetching={p.initialOwner.isFetching} retry={p.admitted ? () => void p.initialOwner.refetch() : undefined} />
        {p.initialOwner.isError && p.initialOwner.data && <p>Designación de la última lectura.</p>}
        {p.initialOwner.data && <><p><strong>{ownerLabel(p.initialOwner.data.status)}</strong> · {p.initialOwner.data.email}</p><p>Designación: <DateText value={p.initialOwner.data.createdAt} /></p></>}
        {p.initialOwner.data?.status === 'pending' && <p>Se vinculará en un nuevo login Google de esa cuenta verificada, mientras la licencia permita operar.</p>}
        {p.initialOwner.data?.status === 'revoked' && <p>La asignación fue revocada. Esta fila histórica no concede acceso ni se reutiliza para cambiar de propietario.</p>}
        {detail.initialOwnerUserId && <p>Propietario inicial registrado. El alta inicial permanece cerrada; no es una herramienta de sustitución de Owner.</p>}
        {!detail.initialOwnerUserId && !p.initialOwner.data && p.initialOwner.isSuccess && <Notice tone="warning">Botica anterior sin asignación pendiente por correo. Requiere regularización explícita; no crees otra botica para corregirla.</Notice>}
        {['suspended', 'cancelled', 'purge_pending', 'purged'].includes(detail.license.status) && <Notice tone="warning">Esta licencia restringe la operación. No se promete acceso al destinatario pendiente hasta resolver su estado mediante el contrato permitido.</Notice>}
        {Date.parse(detail.license.expiresAt) <= p.detail.dataUpdatedAt && <Notice tone="warning">El vencimiento informado ya pasó al consultar. Revisa la licencia; el servidor determina el acceso y el estado operativo.</Notice>}
        <div className="operation-actions"><Button disabled={p.locked || !p.data || !Object.values(licenseActions(detail.license.status)).some(Boolean)} onClick={() => manage('license')}>Gestionar licencia</Button>{!detail.initialOwnerUserId && p.initialOwner.data?.status === 'pending' && <Button variant="secondary" disabled={p.locked || !p.initialOwner.isSuccess || p.initialOwner.isFetching} onClick={() => manage('owner')}>Corregir o revocar Gmail pendiente</Button>}</div>
        <Button variant="secondary" disabled={p.busy || p.cooldown > 0 || !p.admitted || p.detail.isFetching || p.initialOwner.isFetching} onClick={() => void p.refresh()}>Reconsultar licencia y Owner</Button>
      </>}
    </div>
  </Drawer>
}
