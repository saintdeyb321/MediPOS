export function Steps({ labels, current, label = 'Progreso de selección' }: { labels: readonly string[]; current: number; label?: string }) {
  return (
    <ol className="steps" aria-label={label}>
      {labels.map((label, index) => (
        <li key={label} aria-current={index === current ? 'step' : undefined} className={index === current ? 'active' : index < current ? 'complete' : ''}>
          <span aria-hidden="true">{index + 1}</span>{label}
        </li>
      ))}
    </ol>
  )
}
