import { Link } from 'react-router'
import { ApiErrorState, useWorkspace } from '../access'
import { decimalLabel } from '../../shared/api/decimal'
import { PageControls } from '../../shared/ui/PageControls'
import type { useTransferCash } from './useTransferCash'

export function CashSelection({ cash, disabled }: { cash: ReturnType<typeof useTransferCash>; disabled: boolean }) {
  const w = useWorkspace()
  return <section aria-label="Elegir caja de sencillo"><h2>Caja abierta en {w.branch.name}</h2><p>Owner elige una caja consultada de esta sede; Cashier utiliza solamente la propia. Recibir requiere caja abierta en destino; no se abre ninguna automáticamente.</p>
    {cash.choices.isFetching && <p>Consultando cajas…</p>}{cash.choices.isError && <ApiErrorState error={cash.choices.error} title="No pudimos consultar cajas" />}
    <label htmlFor="transfer-cash-session">Caja para sencillo</label><select id="transfer-cash-session" value={cash.id} disabled={disabled || cash.choices.isError || cash.choices.isFetching} onChange={event => cash.setId(event.target.value)}><option value="">Seleccionar caja abierta consultada</option>{!cash.choices.isError && cash.choices.data?.map(row => <option key={row.id} value={row.id}>{row.label}</option>)}</select>
    {cash.choices.data?.length === 0 && !cash.choices.isError && <p>No hay cajas en esta página. <Link to={w.base + '/caja'}>Ir a Caja para gestionar una apertura explícita</Link>.</p>}
    <PageControls size={50} offset={cash.offset} count={cash.owner && !cash.choices.isError ? cash.choices.data?.length : 0} busy={disabled || cash.choices.isFetching} change={value => { cash.setId(''); cash.setOffset(value) }} refresh={() => { cash.setId(''); void cash.choices.refetch() }} />
    {cash.status.isError && <ApiErrorState error={cash.status.error} title="No pudimos consultar estado de caja" />}
    {cash.status.isFetching && <p>Consultando saldo de caja…</p>}
    {cash.session && <p>Caja ABIERTA · Efectivo esperado consultado: {cash.session.expectedCashAmount === null ? 'No disponible; no se puede despachar' : 'S/ ' + decimalLabel(cash.session.expectedCashAmount, 2)}. El servidor vuelve a comprobar saldo y permisos al operar.</p>}
    {cash.status.data?.status === 'closed' && <p>La caja consultada ya está cerrada; vuelve a consultar cajas abiertas.</p>}
  </section>
}
