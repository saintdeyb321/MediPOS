import { TextField } from './TextField'
import { InlineHelp } from './FormLayout'

// This is explicitly a UTC calendar control, not a conversion from the browser's
// timezone. Keep the wire text exact; do not round an instant through Date/number.
export function UtcDateTimeField({ label, value, change, disabled = false, required = false }: {
  label: string; value: string; change: (value: string) => void; disabled?: boolean; required?: boolean
}) {
  const calendarValue = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?(?:Z|\+00:00)$/.test(value) ? value.replace(/(?:Z|\+00:00)$/, '') : ''
  return <div className="form-section-fields"><TextField label={`${label} (UTC)`} type="datetime-local" step="any" value={calendarValue}
    disabled={disabled} required={required && !value} hint="Fecha y hora UTC. No se convierte automáticamente la hora de tu equipo."
    onChange={event => { const text = event.target.value; change(text ? text + (text.length === 16 ? ':00Z' : 'Z') : '') }} />
    {value && !calendarValue && <p>El instante exacto se conserva en la entrada avanzada; no se redondea para adaptarlo al calendario.</p>}
    <InlineHelp title="Entrada UTC avanzada y precisión exacta"><TextField label={`${label}: instante UTC exacto`} value={value} disabled={disabled} maxLength={40} onChange={event => change(event.target.value)} hint="Conserva hasta la precisión admitida por el contrato. El servidor valida el instante y la vigencia." /></InlineHelp>
  </div>
}
