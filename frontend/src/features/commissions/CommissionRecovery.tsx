import { useState } from 'react'
import { Button } from '../../shared/ui/Button'
import { Notice } from '../../shared/ui/Notice'
import { PageControls } from '../../shared/ui/PageControls'
import { decimalLabel } from '../../shared/api/decimal'
import { DateText } from '../../shared/ui/DateText'
import type { useCommissionWrite } from './useCommissionWrite'

export function CommissionRecovery({ write }: { write: ReturnType<typeof useCommissionWrite> }) {
  const [reviewed, setReviewed] = useState(false); const [offset, setOffset] = useState(0)
  if (!write.pending) return null
  return <Notice tone="warning"><div className="operation-body"><h3>Cambio pendiente de conciliación</h3>
    <p>No repitas la solicitud. Una coincidencia de valores no demuestra su finalización y una regla posterior no sustituye la revisión original.</p>
    <Button disabled={write.busy} onClick={() => { setReviewed(false); void write.consult(offset) }}>Consultar operación original</Button>
    {write.observed?.settings && <p>Comisiones {write.observed.settings.isEnabled ? 'habilitadas' : 'deshabilitadas'} · <DateText value={write.observed.settings.updatedAt} /></p>}
    {write.observed?.rules && <><ul>{write.observed.rules.map(rule => <li key={rule.id}>{rule.ruleType === 'fixed' ? 'S/ por unidad base' : 'Porcentaje'}: {decimalLabel(rule.value)} · desde <DateText value={rule.validFrom} /> · creada <DateText value={rule.createdAt} /> · {rule.isActive ? 'Activa' : 'Inactiva'}</li>)}</ul><PageControls size={100} offset={offset} count={write.observed.rules.length} busy={write.busy} change={value => { setOffset(value); setReviewed(false); void write.consult(value) }} refresh={() => { setReviewed(false); void write.consult(offset) }} /></>}
    {write.observed && write.pending !== 'deactivate' && <><label><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Concilié inequívocamente la operación original y su finalización, sin otra escritura en curso.</label><Button disabled={!reviewed || write.busy} onClick={() => { write.acknowledge(); setReviewed(false) }}>Continuar tras conciliación verificada</Button></>}
    {write.pending === 'deactivate' && <p>Solo observar inactiva la misma revisión original permite conciliar una desactivación incierta.</p>}
  </div></Notice>
}
