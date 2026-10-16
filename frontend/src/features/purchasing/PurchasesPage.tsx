import { useState } from 'react'
import { ApiErrorState, useWorkspace } from '../access'
import { Button } from '../../shared/ui/Button'
import { Panel } from '../../shared/ui/Panel'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Notice } from '../../shared/ui/Notice'
import { TextField } from '../../shared/ui/TextField'
import { SuppliersPanel } from './SuppliersPanel'
import { ReceiptEditor } from './ReceiptEditor'
import { PurchaseReview } from './PurchaseReview'
import { PurchaseLookup } from './PurchaseLookup'
import { usePurchaseReceipt } from './usePurchaseReceipt'
import type { Supplier } from './api'
import '../../shared/styles/operations.css'

export function PurchasesPage() {
  const w = useWorkspace()
  if (!['owner', 'pharmacist'].includes(w.access.role)) return <Notice tone="warning">Compras requiere Owner o Pharmacist con acceso operacional vigente.</Notice>
  return <PurchasesWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function PurchasesWorkspace() {
  const w = useWorkspace(); const receipt = usePurchaseReceipt()
  const [supplier, setSupplier] = useState<Supplier | null>(null); const [reference, setReference] = useState('')
  const [supplierBusy, setSupplierBusy] = useState(false)
  const [edit, setEdit] = useState(false); const [reconciled, setReconciled] = useState(false)
  const view = receipt.server
  return <div className="operations"><PageHeader title="Compras" description={`${w.tenantName} · ${w.branch.name} · recepción de mercadería, sin pagos ni SUNAT.`} />
    {receipt.error != null && <ApiErrorState error={receipt.error} title="No pudimos completar la operación de compra" />}
    {receipt.notice && <p role="status">{receipt.notice}</p>}
    {receipt.pending && <Panel><div className="operation-body"><h2>Recepción pendiente de conciliación</h2><p>No repitas la creación, el guardado ni la confirmación. Consulta la misma recepción. Si falta el ID, busca la referencia original y verifica proveedor, fecha y líneas; una coincidencia no demuestra por sí sola idempotencia ni una lectura temprana demuestra rollback.</p>
      {receipt.id && <Button variant="secondary" disabled={receipt.busy} onClick={() => { setReconciled(false); void receipt.consult() }}>Consultar estado de la misma recepción</Button>}
      {view && receipt.pending !== 'confirm' && <><label><input type="checkbox" checked={reconciled} onChange={event => setReconciled(event.target.checked)} /> Verifiqué el registro original, todas sus líneas y la finalización de la solicitud; no queda otra escritura en curso.</label>
        <Button variant="secondary" disabled={!reconciled || receipt.busy} onClick={() => { receipt.acknowledge(); setReconciled(false) }}>Continuar tras conciliación verificada</Button></>}
      {receipt.pending === 'confirm' && <p>Si todavía aparece borrador, la confirmación puede seguir en curso. Solo una lectura confirmada libera este bloqueo; no se vuelve a enviar el POST.</p>}
    </div></Panel>}
    <div className="operation-actions"><Button variant="secondary" disabled={receipt.locked || supplierBusy} onClick={() => { receipt.startNew(); setEdit(false); setReconciled(false); setReference('') }}>Nueva recepción</Button>
      {receipt.id && <Button variant="quiet" disabled={receipt.busy} onClick={() => { setEdit(false); setReconciled(false); void receipt.consult() }}>Actualizar recepción</Button>}</div>
    <SuppliersPanel selected={supplier} select={setSupplier} disabled={receipt.locked || Boolean(receipt.id)} onBusyChange={setSupplierBusy} receiptSupplierId={view?.purchase.supplierId} />
    {!receipt.id && !receipt.pending && <Panel><form className="operation-body" aria-label="Nueva recepción" onSubmit={event => { event.preventDefault(); setEdit(false); void receipt.create(supplier?.id ?? null, reference.trim() || null) }}><h2>Nueva recepción en {w.branch.name}</h2>
      <TextField label="Referencia documental (opcional)" value={reference} maxLength={256} onChange={event => setReference(event.target.value)} hint="Ayuda a recuperar la recepción. No representa una clave de idempotencia ni exige datos fiscales ficticios." />
      <p>Proveedor: {supplier?.name ?? 'Sin proveedor'}. Continuar crea un borrador interno; aún no modifica stock.</p>
      <Button type="submit" disabled={receipt.locked || supplierBusy}>Continuar con productos y lotes</Button>
    </form></Panel>}
    {view && <Panel><div className="operation-body"><PurchaseReview key={JSON.stringify(view)} view={view} disabled={receipt.locked || edit} confirm={() => void receipt.confirm()} />
      {view.purchase.status === 'draft' && view.lines.length > 0 && !edit && <Button variant="secondary" disabled={receipt.locked} onClick={() => setEdit(true)}>Preparar reemplazo de líneas del borrador</Button>}
      {view.purchase.status === 'draft' && (view.lines.length === 0 || edit) && <ReceiptEditor key={view.purchase.id} disabled={receipt.locked} replacing={view.lines.length > 0} save={lines => { void receipt.save(lines)?.then(saved => { if (saved) setEdit(false) }) }} />}
    </div></Panel>}
    <Panel><div className="operation-body"><PurchaseLookup busy={receipt.busy} fixedId={receipt.pending ? receipt.id : ''} consult={id => { setEdit(false); setReconciled(false); void receipt.consult(id) }} /></div></Panel>
  </div>
}
