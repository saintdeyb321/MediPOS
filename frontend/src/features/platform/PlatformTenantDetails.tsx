import { ApiErrorState } from '../access'
import { Button } from '../../shared/ui/Button'
import { DateText } from '../../shared/ui/DateText'
import { Drawer } from '../../shared/ui/Modal'
import { Notice } from '../../shared/ui/Notice'
import { licenseLabel } from '../../shared/ui/licenseLabel'
import { licenseActions, ownerLabel } from './presentation'
import type { PlatformConsoleState } from './PlatformTenantDirectory'

export function PlatformTenantDetails({ p, open, close, manage }: { p: PlatformConsoleState; open: boolean; close: () => void; manage: (task: 'license' | 'owner') => void }) {
  return <Drawer open={open} title="Ficha de botica" onClose={close} busy={p.busy}>
    <div className="operation-body">
      {p.notice && <p role="status">{p.notice}</p>}
      {p.detail.isFetching && <p role="status">Consultando licencia y propietario…</p>}
      {p.detail.isError && <ApiErrorState error={p.detail.error} retry={() => void p.detail.refetch()} />}
      {p.data && <><h3>{p.data.tenant.tradingName}</h3>
        <dl className="summary-grid"><div><dt>Licencia</dt><dd>{licenseLabel(p.data.license.status)}</dd></div><div><dt>Inicio</dt><dd><DateText value={p.data.license.startsAt} /></dd></div><div><dt>Vencimiento</dt><dd><DateText value={p.data.license.expiresAt} /></dd></div><div><dt>Máximo de sucursales</dt><dd>{p.data.license.maxBranches}</dd></div></dl>
        <h3>Primer propietario</h3>
        {p.initialOwner.isFetching && <p role="status">Consultando propietario…</p>}
        {p.initialOwner.isError && <ApiErrorState error={p.initialOwner.error} retry={() => void p.initialOwner.refetch()} />}
        {p.initialOwner.data && <><p><strong>{ownerLabel(p.initialOwner.data.status)}</strong> · {p.initialOwner.data.email}</p><p>Designación: <DateText value={p.initialOwner.data.createdAt} /></p></>}
        {p.initialOwner.data?.status === 'pending' && <p>Se vinculará en un nuevo login Google de esa cuenta verificada, mientras la licencia permita operar.</p>}
        {p.initialOwner.data?.status === 'revoked' && <p>La asignación fue revocada. Esta fila histórica no concede acceso ni se reutiliza para cambiar de propietario.</p>}
        {p.data.initialOwnerUserId && <p>Propietario inicial registrado. El alta inicial permanece cerrada; no es una herramienta de sustitución de Owner.</p>}
        {!p.data.initialOwnerUserId && !p.initialOwner.data && p.initialOwner.isSuccess && <Notice tone="warning">Botica anterior sin asignación pendiente por correo. Requiere regularización explícita; no crees otra botica para corregirla.</Notice>}
        {['suspended', 'cancelled', 'purge_pending', 'purged'].includes(p.data.license.status) && <Notice tone="warning">Esta licencia restringe la operación. No se promete acceso al destinatario pendiente hasta resolver su estado mediante el contrato permitido.</Notice>}
        {Date.parse(p.data.license.expiresAt) <= p.detail.dataUpdatedAt && <Notice tone="warning">El vencimiento informado ya pasó al consultar. Revisa la licencia; el servidor determina el acceso y el estado operativo.</Notice>}
        <div className="operation-actions"><Button disabled={p.locked || !licenseActions(p.data.license.status).renew} onClick={() => manage('license')}>Gestionar licencia</Button>{p.initialOwner.data?.status === 'pending' && <Button variant="secondary" disabled={p.locked} onClick={() => manage('owner')}>Corregir o revocar Gmail pendiente</Button>}</div>
        <Button variant="secondary" disabled={p.busy} onClick={() => void p.refresh()}>Reconsultar licencia y Owner</Button>
      </>}
    </div>
  </Drawer>
}
