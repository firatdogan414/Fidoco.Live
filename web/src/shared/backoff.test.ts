import { describe, expect, it } from 'vitest';
import { backoffDelay } from './backoff';

const noJitter = { initialMs: 1000, maxMs: 30_000, jitter: 0 };

describe('backoffDelay', () => {
  it('doubles the delay for each attempt', () => {
    expect([1, 2, 3, 4].map((a) => backoffDelay(a, noJitter))).toEqual([1000, 2000, 4000, 8000]);
  });

  it('never exceeds the maximum', () => {
    expect(backoffDelay(20, noJitter)).toBe(30_000);
  });

  it('applies jitter within the configured fraction', () => {
    const options = { initialMs: 1000, maxMs: 30_000, jitter: 0.2 };
    expect(backoffDelay(1, options, () => 0)).toBe(800);
    expect(backoffDelay(1, options, () => 1)).toBe(1200);
  });
});
