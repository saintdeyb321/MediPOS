import { useState } from 'react'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { FormFooter, InlineHelp } from '../../shared/ui/FormLayout'
import { licensePlanMonths } from './api'
import type { PlanMonths, TenantCreationInput } from './api'

export function NewTenantForm({ locked, create, dirty }: { locked: boolean; create: (input: TenantCreationInput) => Promise<boolean | undefined>; dirty: (value: boolean) => void }) {
  const [name, setName] = useState(''); const [email, setEmail] = useState(''); const [branches, setBranches] = useState(2)
  const [termMonths, setTermMonths] = useState<0 | PlanMonths>(0)
  const validName = name.trim().length > 0 && name.trim().length <= 256
  const validEmail = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim()) && email.trim().length <= 320
  const validLicense = Number.isInteger(branches) && branches >= 1 && branches <= 5
  function save() {
    if (locked || !validName || !validEmail || !validLicense) return
    void create({ tradingName: name.trim(), ownerEmail: email.trim(), maxBranches: branches, termMonths })
  }
  return <form className="operation-body" onSubmit={event => event.preventDefault()}>
    <fieldset disabled={locked}>
      <div className="form-section-fields">
        <TextField label="Nombre comercial" value={name} maxLength={256} onChange={e => { setName(e.target.value); dirty(Boolean(e.target.value || email || branches !== 2 || termMonths !== 0)) }} />
        <TextField label="Gmail del propietario" type="email" value={email} maxLength={320} autoComplete="off" onChange={e => { setEmail(e.target.value); dirty(Boolean(name || e.target.value || branches !== 2 || termMonths !== 0)) }} hint="Verifica el Gmail exacto antes de crear la botica. Puede ser una cuenta que aún no ingresó a MediPOS." />
        <TextField label="Máximo de sucursales" type="number" min={1} max={5} step={1} value={branches} onChange={e => { setBranches(Number(e.target.value)); dirty(Boolean(name || email || Number(e.target.value) !== 2 || termMonths !== 0)) }} />
        <label>Plan de licencia<select value={termMonths} onChange={event => {
          const value = Number(event.target.value) as 0 | PlanMonths
          setTermMonths(value); dirty(Boolean(name || email || branches !== 2 || value !== 0))
        }}>
          <option value={0}>Prueba gratuita · 7 días</option>
          {licensePlanMonths.map(months => <option key={months} value={months}>{months} {months === 1 ? 'mes' : 'meses'}</option>)}
        </select></label>
        <dl className="summary-grid"><div><dt>Botica</dt><dd>{name.trim() || 'Por indicar'}</dd></div><div><dt>Propietario pendiente</dt><dd>{email.trim() || 'Por indicar'}</dd></div><div><dt>Licencia</dt><dd>{termMonths === 0 ? 'Prueba de 7 días' : termMonths === 1 ? '1 mes' : `${termMonths} meses`} · {validLicense ? branches : '—'} sucursales</dd></div></dl>
        <p>La botica, licencia y asignación pendiente se guardarán juntas. El primer login Google verificado vinculará al propietario.</p>
      </div>
    </fieldset>
    <FormFooter><Button disabled={locked || !validName || !validEmail || !validLicense} onClick={save}>Crear botica y asignación pendiente</Button>
    </FormFooter>
    <InlineHelp title="¿Cómo recibe acceso el propietario?">Comparte la dirección habitual de MediPOS. No necesita invitación, identificador ni un ingreso previo. Ser Owner no concede autoridad de plataforma.</InlineHelp>
  </form>
}
