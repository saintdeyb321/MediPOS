import { useId } from 'react'
import { decimalLabel } from '../api/decimal'
import type { JsonNumber } from '../api/exactJson'

export interface BarDatum { key: string; label: string; value: JsonNumber; formatted: string; tone?: 'primary' | 'info' | 'success' | 'warning' | 'error' }

export function BarChart({ title, description, rows }: { title: string; description: string; rows: readonly BarDatum[] }) {
  const titleId = useId()
  if (rows.length < 2) return <section className="chart-card" aria-labelledby={titleId}><div className="chart-heading"><h3 id={titleId}>{title}</h3><p>{description}</p></div>{rows[0] ? <p className="single-observation">{rows[0].label}: <strong>{rows[0].formatted}</strong><small>Una sola observación; no se dibuja una comparación.</small></p> : <p className="chart-empty">Sin datos para esta consulta.</p>}</section>
  const literals = rows.map(row => decimalLabel(row.value).replace(/^-/, '').split('.'))
  const scale = Math.max(0, ...literals.map(parts => parts[1]?.length ?? 0))
  const magnitudes = literals.map(([whole, fraction = '']) => BigInt(whole + fraction.padEnd(scale, '0')))
  const max = magnitudes.reduce((largest, value) => value > largest ? value : largest, 0n)
  return <section className="chart-card" aria-labelledby={titleId}><div className="chart-heading"><h3 id={titleId}>{title}</h3><p>{description}</p></div>
    {rows.length ? <ul className="chart-bars">{rows.map((row, index) => <li key={row.key}>
      <div className="chart-label"><span>{row.label}</span><strong>{row.formatted}</strong></div>
      <div className="chart-track" aria-hidden="true"><span className={`chart-fill chart-${row.tone ?? 'primary'}`} style={{ width: `${max ? Number(magnitudes[index]! * 10000n / max) / 100 : 0}%` }} /></div>
    </li>)}</ul> : <p className="chart-empty">Sin datos para esta consulta.</p>}
    {!!rows.length && max === 0n && <p className="chart-empty">Todos los valores consultados son cero.</p>}
    {rows.some(row => row.value.literal.startsWith('-')) && <p className="chart-empty">Las barras representan magnitudes; el signo se conserva en cada importe.</p>}
  </section>
}
