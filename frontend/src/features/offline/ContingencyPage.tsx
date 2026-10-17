import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { useRuntime } from '../access'
import { Button } from '../../shared/ui/Button'
import { Panel } from '../../shared/ui/Panel'
import { TextField } from '../../shared/ui/TextField'
import { decimalLabel } from '../../shared/api/decimal'
import type { CatalogItem } from './api'
import { leaseValid, offlineStorage } from './storage'
import type { OfflineLease, OutboxRow } from './storage'
import { offlineAmounts, queueOfflineSale, synchronizeOutbox } from './workflow'
import type { OfflineCartLine } from './workflow'
import { OutboxTable } from './OfflinePage'

// Separate contingency entry: it never bypasses the online SessionGate or calls online sale confirmation.
export function ContingencyPage() {
  const { http, service } = useRuntime()
  const [lease, setLease] = useState<OfflineLease | null>(null); const [items, setItems] = useState<CatalogItem[]>([]); const [rows, setRows] = useState<OutboxRow[]>([])
  const [ready, setReady] = useState(false); const [error, setError] = useState(''); const [query, setQuery] = useState(''); const [lines, setLines] = useState<OfflineCartLine[]>([])
  const [method, setMethod] = useState('cash'); const [payment, setPayment] = useState(''); const [reviewed, setReviewed] = useState(false); const [busy, setBusy] = useState(false); const writing = useRef(false); const alive = useRef(true)
  const observedNonce = useRef('')
  useEffect(() => {
    alive.current = true; let running = false
    async function refresh() {
      if (running) return; running = true
      try {
        const current = await offlineStorage.lease()
        if (!current || !leaseValid(current)) {
          if (current) await offlineStorage.lock()
          if (alive.current) { setLease(null); setItems([]); setRows([]); setLines([]) }
          return
        }
        await offlineStorage.touch(current)
        const catalog = await offlineStorage.catalog(current); const outbox = await offlineStorage.outbox(current)
        if (alive.current) {
          if (observedNonce.current && observedNonce.current !== current.nonce) { setLines([]); setPayment(''); setReviewed(false) }
          observedNonce.current = current.nonce; setLease(current); setItems(catalog); setRows(outbox)
        }
      } catch { if (alive.current) { setLease(null); setItems([]); setRows([]); setLines([]); setError('No se pudo leer de forma segura la contingencia local. Reconecta y verifica tu sesión.') } }
      finally { running = false; if (alive.current) setReady(true) }
    }
    void refresh(); const timer = window.setInterval(() => void refresh(), 2000)
    const channel = typeof BroadcastChannel !== 'undefined' ? new BroadcastChannel('medipos-contingency-v1') : null
    if (channel) channel.onmessage = () => { void refresh() }
    return () => { alive.current = false; clearInterval(timer); channel?.close() }
  }, [])
  useEffect(() => {
    if (!lease || !http) return
    const abort = new AbortController(); let running = false
    const sync = async () => {
      if (!navigator.onLine || running || abort.signal.aborted) return
      running = true
      try { await synchronizeOutbox(lease, service, http, abort.signal); const updated = await offlineStorage.outbox(lease, true); if (!abort.signal.aborted) setRows(updated) }
      catch { if (!abort.signal.aborted) setError('La cola conserva sus UUID. La sincronización no se pudo confirmar; reconecta, verifica tu sesión y consulta los eventos originales.') }
      finally { running = false }
    }
    const timer = window.setInterval(() => void sync(), 10_000); window.addEventListener('online', sync)
    return () => { abort.abort(); clearInterval(timer); window.removeEventListener('online', sync) }
  // Lease identity stays fixed while touch refreshes its observed clock.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lease?.nonce, http, service])
  let total = ''; let invalid = ''
  try { if (lines.length) total = decimalLabel(offlineAmounts(lines).total, 2) } catch (failure) { invalid = failure instanceof Error ? failure.message : 'Carrito inválido.' }
  const matches = items.filter(item => {
    const p = item.product; if (!p?.isActive) return false
    const text = [p.name, p.internalCode, p.barcode, p.brandOrLaboratory, p.medicine?.normalizedStrength, p.medicine?.dosageForm, ...(p.medicine?.activeIngredients ?? [])].join(' ').toLocaleLowerCase('es-PE')
    return query.trim().length >= 2 && text.includes(query.trim().toLocaleLowerCase('es-PE'))
  }).slice(0, 20)
  async function queue() {
    if (!lease || writing.current || !reviewed || !lines.length || invalid) return
    writing.current = true; setBusy(true); setError('')
    try {
      await queueOfflineSale(lease, lines, method, payment)
      const pending = await offlineStorage.outbox(lease)
      if (alive.current) { setRows(pending); setLines([]); setPayment(''); setReviewed(false) }
    } catch (failure) { if (alive.current) setError(failure instanceof Error ? failure.message : 'No se pudo guardar en la cola local.') }
    finally { writing.current = false; if (alive.current) setBusy(false) }
  }
  return <main className="operations"><h1>POS de contingencia</h1><p>Registro local provisional. No confirma stock, venta ni pago en el servidor. Los cambios de permiso, horario, licencia, caja y existencias se verifican al reconectar; pueden producir conflicto.</p>
    {!ready && <p role="status">Leyendo preparación local…</p>}{error && <p role="alert">{error}</p>}
    {ready && !lease && <Panel><p>No hay una preparación vigente. Inicia Google con conexión, entra a tu sucursal, abre tu caja y prepara la contingencia. No se extiende la sesión sin servidor.</p><Link to="/">Ingresar con conexión</Link></Panel>}
    {lease && <><Panel><div className="operation-body"><h2>{lease.tenantName} · {lease.branchName}</h2><p>Vence: {new Date(lease.expiresAt).toISOString()}. Catálogo de una fotografía anterior; cantidades orientativas, sin reserva.</p><Link to={`/boticas/${lease.tenantId}/sucursales/${lease.branchId}/sincronizacion`}>Volver online y conciliar con identidad verificada</Link><Button variant="quiet" onClick={() => { void offlineStorage.lock().then(() => { setLease(null); setItems([]); setRows([]); setLines([]) }) }}>Bloquear contingencia local</Button></div></Panel>
      <Panel><div className="operation-body"><TextField label="Buscar en catálogo descargado" value={query} onChange={event => setQuery(event.target.value)} />
        {query.trim().length >= 2 && !matches.length && <p>No hay coincidencias en el catálogo descargado.</p>}
        {matches.map(item => <section key={item.businessProductId}><h3>{item.product?.name}</h3><p>{item.product?.medicine?.activeIngredients.join(' + ')} · {item.product?.medicine?.normalizedStrength} · {item.product?.medicine?.dosageForm}</p><p>Precio por unidad base: S/ {decimalLabel(item.product!.retailPrice, 2)} · stock orientativo base: {item.stock && decimalLabel(item.stock.sellableStockBase)}</p>
          {item.product?.units.filter(unit => unit.isActive).map(unit => <Button key={unit.productUnitId} variant="secondary" disabled={busy || lines.some(line => line.item.businessProductId === item.businessProductId && line.unitId === unit.productUnitId)} onClick={() => { setReviewed(false); setLines(current => [...current, { item, unitId: unit.productUnitId, quantity: '1' }]) }}>{unit.name}</Button>)}</section>)}
      </div></Panel>
      <Panel><div className="operation-body"><h2>Carrito local de contingencia</h2>{lines.map((line, index) => <section key={line.item.businessProductId + '/' + line.unitId}><p>{line.item.product?.name} · {line.item.product?.units.find(unit => unit.productUnitId === line.unitId)?.name}</p><TextField label={`Cantidad ${index + 1}`} value={line.quantity} inputMode="decimal" disabled={busy} onChange={event => { setReviewed(false); setLines(current => current.map((row, i) => i === index ? { ...row, quantity: event.target.value } : row)) }} /><Button variant="quiet" disabled={busy} onClick={() => { setReviewed(false); setLines(current => current.filter((_, i) => i !== index)) }}>Quitar línea {index + 1}</Button></section>)}
        {invalid && <p role="alert">{invalid}</p>}{total && <p>Total provisional: S/ {total}</p>}<label>Medio declarado<select value={method} disabled={busy} onChange={event => { setMethod(event.target.value); setReviewed(false) }}>{[['cash', 'Efectivo'], ['yape', 'Yape'], ['plin', 'Plin'], ['card', 'Tarjeta'], ['transfer', 'Transferencia']].map(([v, label]) => <option key={v} value={v}>{label}</option>)}</select></label>
        <TextField label="Importe declarado, sin procesamiento externo" value={payment} disabled={busy} inputMode="decimal" onChange={event => { setPayment(event.target.value); setReviewed(false) }} /><label><input type="checkbox" disabled={busy} checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Revisé el importe y autorizo guardar este evento pendiente y sincronizarlo al recuperar conexión con esta misma cuenta verificada. No es una confirmación del servidor.</label><Button disabled={busy || !reviewed || !total || Boolean(invalid)} onClick={() => void queue()}>Guardar una vez en outbox local</Button>
      </div></Panel><Panel><OutboxTable rows={rows} /></Panel></>}
  </main>
}
