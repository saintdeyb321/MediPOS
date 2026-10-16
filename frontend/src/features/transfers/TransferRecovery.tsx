import { useState } from 'react'
import { isUuid } from '../access'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Notice } from '../../shared/ui/Notice'

export function TransferRecovery({ id, pending, unidentified, busy, observed, consult, acknowledge }: { id: string; pending: string | null; unidentified: boolean; busy: boolean; observed: boolean; consult: (id?: string) => Promise<void>; acknowledge: () => void }) {
  const [target, setTarget] = useState(''); const [reviewed, setReviewed] = useState(false)
  return <section aria-label="Recuperar transferencia"><h2>Consultar / recuperar transferencia</h2>
    {pending && <Notice tone="warning">Hay una operación pendiente de conciliación. No se vuelve a enviar. Una lectura temprana no demuestra rollback. Conserva la dirección de esta página al recargar.</Notice>}
    {pending && !unidentified && <Button disabled={busy || !id} onClick={() => { setReviewed(false); void consult() }}>Consultar la misma transferencia</Button>}
    {(!pending || unidentified) && <><TextField label="Identificador de transferencia existente" value={target} onChange={event => setTarget(event.target.value.trim())} maxLength={36} autoComplete="off" />
      <Button disabled={busy || !isUuid(target)} onClick={() => { setReviewed(false); void consult(target) }}>Consultar identificador existente</Button></>}
    {unidentified && <><p>Se perdió el resultado de una creación sin identificador confirmado. Consulta el listado y el detalle; coincidencias de productos, importe o fechas no son una clave de idempotencia. Si no puedes identificar inequívocamente la operación original y su finalización, mantén el bloqueo y concilia con Owner.</p>
      {observed && <><label><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Verifiqué inequívocamente la operación original y su finalización, sin otra creación en curso. No basta una coincidencia del listado.</label>
        <Button disabled={busy || !reviewed} onClick={() => { acknowledge(); setReviewed(false) }}>Continuar tras conciliación de creación</Button></>}
    </>}
  </section>
}
