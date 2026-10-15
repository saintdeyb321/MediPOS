// Decimal JSON tokens remain lexical values: no conversion through IEEE-754 Number.
export class JsonNumber {
  readonly literal: string
  constructor(literal: string) {
    if (!/^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$/.test(literal)) throw new TypeError('Invalid JSON number')
    this.literal = literal
    Object.freeze(this)
  }
  valueOf(): never { throw new TypeError('Use the exact decimal literal, not Number arithmetic') }
}

type SourceContext = { source?: string }
export function parseExactJson(text: string): unknown {
  return JSON.parse(text, (_key: string, value: unknown, context?: SourceContext): unknown => {
    if (typeof value !== 'number') return value
    if (!context?.source) throw new TypeError('Exact JSON numbers are unsupported in this browser')
    return new JsonNumber(context.source)
  }) as unknown
}

// The backend expects numeric JSON tokens, not quoted decimal strings.
export function stringifyExactJson(body: unknown): string {
  const raw = (JSON as typeof JSON & { rawJSON?: (literal: string) => unknown }).rawJSON
  const ancestors = new Set<object>()
  function validate(value: unknown): void {
    if (value instanceof JsonNumber) { if (!raw) throw new TypeError('Exact JSON serialization unsupported'); return }
    if (value === null || typeof value === 'string' || typeof value === 'boolean') return
    if (typeof value === 'number') { if (!Number.isSafeInteger(value)) throw new TypeError('Use JsonNumber for decimals'); return }
    if (typeof value !== 'object' || ancestors.has(value)) throw new TypeError('Invalid JSON value')
    if (!Array.isArray(value) && Object.getPrototypeOf(value) !== Object.prototype) throw new TypeError('Plain JSON objects required')
    ancestors.add(value)
    for (const item of Array.isArray(value) ? value : Object.values(value)) validate(item)
    ancestors.delete(value)
  }
  validate(body)
  return JSON.stringify(body, (_key: string, value: unknown) => value instanceof JsonNumber ? raw!(value.literal) : value)
}
