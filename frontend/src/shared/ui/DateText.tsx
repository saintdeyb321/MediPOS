export function DateText({ value, dateOnly = false }: { value: string | null | undefined; dateOnly?: boolean }) {
  if (!value) return <span>No indicado</span>
  // Calendar dates stay on their original day; only instants are shown in Lima.
  const date = new Date(dateOnly ? `${value}T00:00:00Z` : value)
  if (Number.isNaN(date.getTime())) return <time dateTime={value}>{value}</time>
  const label = new Intl.DateTimeFormat('es-PE', {
    timeZone: dateOnly ? 'UTC' : 'America/Lima', dateStyle: 'medium', ...(dateOnly ? {} : { timeStyle: 'short' as const }),
  }).format(date)
  return <time dateTime={value} title={dateOnly ? 'Fecha de calendario' : 'Hora de Lima'}>{label}</time>
}
