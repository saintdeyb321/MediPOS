import { useId } from 'react'
import type { InputHTMLAttributes, ReactNode } from 'react'
import { Search } from 'lucide-react'

export function FormField({ id, label, hint, error, children }: { id: string; label: string; hint?: string; error?: string; children: ReactNode }) {
  return (
    <div className="form-field">
      <label htmlFor={id}>{label}</label>
      {children}
      {hint && <p id={`${id}-hint`}>{hint}</p>}
      {error && <p id={`${id}-error`} className="field-error" role="alert">{error}</p>}
    </div>
  )
}

type TextFieldProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'size'> & { label: string; hint?: string; error?: string; leading?: ReactNode }

export function TextField({ label, hint, error, leading, id: providedId, ...props }: TextFieldProps) {
  const generatedId = useId()
  const id = providedId ?? generatedId
  const describedBy = [props['aria-describedby'], hint && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(' ') || undefined
  return (
    <FormField id={id} label={label} hint={hint} error={error}>
      <div className={`input-shell${error ? ' invalid' : ''}`}>
        {leading && <span aria-hidden="true">{leading}</span>}
        <input {...props} id={id} aria-describedby={describedBy} aria-invalid={error ? true : props['aria-invalid']} />
      </div>
    </FormField>
  )
}

export function SearchField(props: Omit<TextFieldProps, 'leading' | 'type'>) {
  return <TextField {...props} type="search" leading={<Search size={20} />} />
}
