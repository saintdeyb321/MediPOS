import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { ClipboardList, PackageCheck } from 'lucide-react'
import { ApiErrorState, useWorkspace } from '../access'
import { Button } from '../../shared/ui/Button'
import { Panel } from '../../shared/ui/Panel'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Notice } from '../../shared/ui/Notice'
import { TextField } from '../../shared/ui/TextField'
import { Steps } from '../../shared/ui/Steps'
import { InlineHelp } from '../../shared/ui/FormLayout'
import { Badge } from '../../shared/ui/Badge'
import { decimalLabel } from '../../shared/api/decimal'
import { receiptTotal } from './receipt'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
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
  const [parameters] = useSearchParams()
  const [supplier, setSupplier] = useState<Supplier | null>(null); const [reference, setReference] = useState('')
  const [supplierBusy, setSupplierBusy] = useState(false)
  const [edit, setEdit] = useState(false); const [reconciled, setReconciled] = useState(false)
  const [starting, setStarting] = useState(false); const [lineStep, setLineStep] = useState<1 | 2>(1)
  const [newRequested, setNewRequested] = useState(false)
  const view = receipt.server
  const step = view?.purchase.status === 'confirmed' ? 4 : view && view.lines.length > 0 && !edit ? 3 : receipt.id ? lineStep : 0
  function startNew() { receipt.startNew(); setStarting(true); setLineStep(1); setEdit(false); setReconciled(false); setReference(''); setNewRequested(false) }
  return <div className="operations"><PageHeader icon={ClipboardList} eyebrow="Abastecimiento · recepción guiada" title="Compras" description={`Recepción de mercadería · ${w.branch.name}`} action={<Button disabled={receipt.locked || supplierBusy} onClick={() => { if (starting || receipt.id) setNewRequested(true); else startNew() }}>Nueva recepción</Button>} />
    <ConfirmDialog open={newRequested} title="Preparar otra recepción" confirmLabel="Continuar con otra recepción" disabled={receipt.locked || supplierBusy} onClose={() => setNewRequested(false)} onConfirm={startNew}><p>Las líneas que aún no guardaste se descartarán. La recepción ya persistida conserva su estado y no se elimina ni se confirma.</p></ConfirmDialog>
    {(starting || receipt.id || receipt.pending) && <Steps labels={['Proveedor y referencia', 'Productos', 'Lotes y costos', 'Revisión', 'Recepción confirmada']} current={step} label="Progreso de la recepción" focusOnChange />}
    {receipt.error != null && <ApiErrorState error={receipt.error} title="No pudimos completar la operación de compra" />}
    {receipt.notice && <p role="status">{receipt.notice}</p>}
    {receipt.pending && <Panel><div className="operation-body"><h2>Recepción pendiente de conciliación</h2><p>No repitas la creación, el guardado ni la confirmación. Consulta la misma recepción. Si falta el ID, busca la referencia original y verifica proveedor, fecha y líneas; una coincidencia no demuestra por sí sola idempotencia ni una lectura temprana demuestra rollback.</p>
      {receipt.id && <Button variant="secondary" disabled={receipt.busy} onClick={() => { setReconciled(false); void receipt.consult() }}>Consultar estado de la misma recepción</Button>}
      {view && receipt.pending !== 'confirm' && <><label><input type="checkbox" checked={reconciled} onChange={event => setReconciled(event.target.checked)} /> Verifiqué el registro original, todas sus líneas y la finalización de la solicitud; no queda otra escritura en curso.</label>
        <Button variant="secondary" disabled={!reconciled || receipt.busy} onClick={() => { receipt.acknowledge(); setReconciled(false) }}>Continuar tras conciliación verificada</Button></>}
      {receipt.pending === 'confirm' && <p>Si todavía aparece borrador, la confirmación puede seguir en curso. Solo una lectura confirmada libera este bloqueo; no se vuelve a enviar el POST.</p>}
    </div></Panel>}
    {receipt.id && <Button variant="quiet" disabled={receipt.busy} onClick={() => { setEdit(false); setReconciled(false); void receipt.consult() }}>Actualizar recepción</Button>}
    <Panel><div className="operation-body"><details open={!starting && !receipt.id}><summary>Buscar una recepción guardada</summary><PurchaseLookup busy={receipt.busy} fixedId={receipt.pending ? receipt.id : ''} consult={id => { setEdit(false); setReconciled(false); void receipt.consult(id) }} /></details></div></Panel>
    <div className="guided-layout" hidden={!starting && !receipt.id && !receipt.pending && !parameters.has('supplierPending')}><div className="guided-main"><SuppliersPanel selected={supplier} select={setSupplier} disabled={receipt.locked || Boolean(receipt.id)} onBusyChange={setSupplierBusy} receiptSupplierId={view?.purchase.supplierId} />
    {starting && !receipt.id && !receipt.pending && <Panel><form className="operation-body" aria-label="Nueva recepción" onSubmit={event => { event.preventDefault(); setEdit(false); void receipt.create(supplier?.id ?? null, reference.trim() || null) }}><h2>1. Proveedor y referencia</h2>
      <TextField label="Referencia documental (opcional)" value={reference} maxLength={256} onChange={event => setReference(event.target.value)} hint="Ayuda a recuperar la recepción. No representa una clave de idempotencia ni exige datos fiscales ficticios." />
      <p>Proveedor: {supplier?.name ?? 'Sin proveedor'}. Continuar crea un borrador interno; aún no modifica stock.</p>
      <Button type="submit" disabled={receipt.locked || supplierBusy}>Continuar con productos y lotes</Button>
    </form></Panel>}
    {view && <Panel><div className="operation-body"><div hidden={view.purchase.status === 'draft' && (edit || !view.lines.length)}><PurchaseReview key={JSON.stringify(view)} view={view} disabled={receipt.locked || edit} confirm={() => void receipt.confirm()} /></div>
      {view.purchase.status === 'draft' && view.lines.length > 0 && !edit && <Button variant="secondary" disabled={receipt.locked} onClick={() => { setEdit(true); setLineStep(1) }}>Editar conjunto de líneas del borrador</Button>}
      {view.purchase.status === 'draft' && (view.lines.length === 0 || edit) && <ReceiptEditor key={view.purchase.id} disabled={receipt.locked} stage={lineStep} onStageChange={setLineStep} replacing={view.lines.length > 0} save={lines => { void receipt.save(lines)?.then(saved => { if (saved) setEdit(false) }) }} />}
    </div></Panel>}
    </div><aside className="guided-summary"><Panel><div className="operation-body"><span className="module-icon"><PackageCheck size={24} aria-hidden="true" /></span><h2>Resumen de recepción</h2><Badge tone={receipt.pending ? 'warning' : view?.purchase.status === 'confirmed' ? 'success' : 'info'}>{receipt.pending ? 'Por conciliar' : view?.purchase.status === 'confirmed' ? 'Confirmada' : receipt.id ? 'Borrador persistido' : 'Sin guardar'}</Badge>
      <dl className="summary-grid"><div><dt>Sucursal destino</dt><dd>{w.branch.name}</dd></div><div><dt>Referencia</dt><dd>{view?.purchase.documentReference ?? (reference || 'Sin referencia')}</dd></div><div><dt>Líneas persistidas</dt><dd>{view ? view.lines.length : '—'}</dd></div><div><dt>Importe de líneas guardadas</dt><dd>{view ? `S/ ${decimalLabel(receiptTotal(view.lines), 2)}` : '—'}</dd></div></dl>
      <p className="data-caption">Las líneas locales del editor no se incluyen hasta guardarlas. El stock solo ingresa al confirmar la recepción.</p>
    </div></Panel></aside></div>
    <InlineHelp title="Qué registra una recepción">La API permite recuperar recepciones por referencia o identificador, no un historial general. Solo confirmar la recepción crea lotes y movimientos de stock; no genera pagos ni comprobantes SUNAT.</InlineHelp>
  </div>
}
