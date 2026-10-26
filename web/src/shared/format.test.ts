import { describe, expect, it } from 'vitest';
import { countdown, formatMoney, fromLocalInput, parseKronor, toLocalInput } from './format';

describe('formatMoney', () => {
  it('formats öre as kronor', () => {
    expect(formatMoney(19900).replace(/\s/g, ' ')).toBe('199 kr');
    expect(formatMoney(19950).replace(/\s/g, ' ')).toBe('199,50 kr');
    expect(formatMoney(1990000).replace(/\s/g, ' ')).toBe('19 900 kr');
  });
});

describe('countdown', () => {
  it('splits the remaining time', () => {
    const now = Date.parse('2026-11-14T16:00:00Z');
    expect(countdown(now, '2026-11-15T17:01:05Z')).toEqual({ days: 1, hours: 1, minutes: 1, seconds: 5, done: false });
  });

  it('is done when the target has passed', () => {
    expect(countdown(Date.parse('2026-11-14T18:00:01Z'), '2026-11-14T18:00:00Z')).toEqual({ days: 0, hours: 0, minutes: 0, seconds: 0, done: true });
  });
});

describe('parseKronor', () => {
  it('parses whole and decimal kronor', () => {
    expect(parseKronor('199')).toBe(19900);
    expect(parseKronor('199,50')).toBe(19950);
    expect(parseKronor(' 1 990 ')).toBe(199000);
  });

  it('rejects invalid input', () => {
    expect(parseKronor('')).toBeNull();
    expect(parseKronor('abc')).toBeNull();
    expect(parseKronor('1,999')).toBeNull();
    expect(parseKronor('-5')).toBeNull();
  });
});

describe('local datetime input', () => {
  it('round-trips through ISO', () => {
    const local = '2026-11-14T19:00';
    expect(toLocalInput(fromLocalInput(local))).toBe(local);
  });
});
