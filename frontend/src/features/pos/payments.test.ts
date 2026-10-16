// @vitest-environment node
import { expect, it } from 'vitest'
import { JsonNumber, stringifyExactJson } from '../../shared/api/exactJson'
import { emptyPayments, paymentInputs } from './payments'

it('keeps mixed payments exact and omits unused methods without inventing change', () => {
  const result = paymentInputs({ ...emptyPayments(), cash: '0,10', yape: '0.20' }, new JsonNumber('0.30'))
  expect(result.error).toBeNull()
  expect(stringifyExactJson(result.payments)).toBe('[{"method":"cash","amount":0.10},{"method":"yape","amount":0.20}]')
})
it('preserves maximum monetary precision without IEEE754 conversion', () => {
  const result = paymentInputs({ ...emptyPayments(), transfer: '99999999999999.9999' }, new JsonNumber('99999999999999.9999'))
  expect(result.error).toBeNull()
  expect(result.sum.literal).toBe('99999999999999.9999')
})
it.each(['0', '-1', '1e2', '0.00001', '100000000000000', 'NaN'])('rejects invalid selected payment %s', amount => {
  expect(paymentInputs({ ...emptyPayments(), cash: amount }, new JsonNumber('1')).error).not.toBeNull()
})
it('requires at least one payment and rejects under/over payment', () => {
  expect(paymentInputs(emptyPayments(), new JsonNumber('1')).error).toMatch(/al menos/)
  for (const cash of ['0.99', '1.01']) expect(paymentInputs({ ...emptyPayments(), cash }, new JsonNumber('1')).error).toMatch(/exactamente/)
})
