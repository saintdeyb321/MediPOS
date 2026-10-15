import type { ComponentProps } from 'react'

export function Panel({ className = '', children, ...props }: ComponentProps<'section'>) {
  return <section {...props} className={`panel ${className}`}>{children}</section>
}
