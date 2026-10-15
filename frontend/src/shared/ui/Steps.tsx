export function Steps({ labels, current }: { labels: readonly string[]; current: number }) {
  return (
    <ol className="steps" aria-label="Progreso de selección">
      {labels.map((label, index) => (
        <li key={label} aria-current={index === current ? 'step' : undefined} className={index === current ? 'active' : index < current ? 'complete' : ''}>
          <span aria-hidden="true">{index + 1}</span>{label}
        </li>
      ))}
    </ol>
  )
}
