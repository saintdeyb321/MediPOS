import type { ButtonHTMLAttributes, ReactNode } from 'react'

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'primary' | 'secondary' | 'quiet' | 'ghost' | 'danger' | 'success'
  size?: 'sm' | 'md' | 'lg'
  pending?: boolean
}

export function Button({ variant = 'primary', size = 'md', pending = false, disabled, className = '', children, ...props }: ButtonProps) {
  return (
    <button type="button" {...props} className={`button ${variant} button-${size} ${className}`} disabled={disabled || pending} aria-busy={pending || undefined}>
      {children}
    </button>
  )
}

export function IconButton({ label, children, className = '', ...props }: Omit<ButtonProps, 'variant'> & { label: string; children: ReactNode }) {
  return <Button {...props} variant="quiet" className={`icon-button ${className}`} aria-label={label}>{children}</Button>
}
