import { useId } from 'react'
import type { ReactNode } from 'react'

export function FormSection({ title, description, children }: { title: string; description?: string; children: ReactNode }) {
  const id = useId()
  return <section className="form-section" aria-labelledby={id}>
    <div className="form-section-heading"><h3 id={id}>{title}</h3>{description && <p>{description}</p>}</div>
    <div className="form-section-fields">{children}</div>
  </section>
}

export function FormFooter({ children }: { children: ReactNode }) {
  return <div className="form-footer">{children}</div>
}

export function InlineHelp({ title = 'Más información', children }: { title?: string; children: ReactNode }) {
  return <details className="inline-help"><summary>{title}</summary><div>{children}</div></details>
}
