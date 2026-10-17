import { expect, it } from 'vitest'
import { JsonNumber, parseExactJson } from '../../shared/api/exactJson'
import { canonicalJson, payloadHash } from './canonical'

// Vectors shared with SyncDomainTests (server SyncPayload v1), not an integration-server substitute.
it('matches the server ordinal, exact numeric and Unicode canonical representation', async () => {
  expect(canonicalJson(parseExactJson('{"b":"<é>","a":1.0}'))).toBe('{"a":1e0,"b":"<\\u00E9>"}')
  const first = canonicalJson(parseExactJson('{"z":[-0.00,1.00,9999999999999999.999999999999],"a":{"b":"é"}}'))
  const second = canonicalJson(parseExactJson(' { "a":{"b":"\\u00e9"}, "z":[0,1e0,9999999999999999999999999999e-12] }'))
  expect(first).toBe(second); expect(await payloadHash(first)).toBe(await payloadHash(second))
  expect(canonicalJson({ x: new JsonNumber('1e-29') })).not.toBe(canonicalJson({ x: new JsonNumber('0') }))
})
it('distinguishes clinical Unicode and preserves literal escape sequences', () => {
  expect(canonicalJson({ x: 'µg' })).toBe('{"x":"\\u00B5g"}')
  expect(canonicalJson({ x: 'μg' })).toBe('{"x":"\\u03BCg"}')
  expect(canonicalJson({ x: '\\n\\u00ff\n\t' })).toBe('{"x":"\\\\n\\\\u00ff\\u000A\\u0009"}')
  expect(canonicalJson({ x: '😀' })).toBe('{"x":"\\uD83D\\uDE00"}')
})
it.each(['\0', '\ud800', '\udfff'])('rejects invalid PostgreSQL Unicode %j', x => expect(() => canonicalJson({ x })).toThrow())
it('rejects floating point, empty document and oversized payloads', () => {
  expect(() => canonicalJson({ n: 0.1 })).toThrow(); expect(() => canonicalJson({})).toThrow()
  expect(() => canonicalJson({ data: 'x'.repeat(65_536) })).toThrow()
})
