export interface BackoffOptions {
  /** First delay in milliseconds. */
  initialMs: number;
  /** Upper bound for any delay in milliseconds. */
  maxMs: number;
  /** Random jitter as a fraction of the delay (0–1). */
  jitter: number;
}

export const defaultBackoff: BackoffOptions = { initialMs: 1000, maxMs: 30_000, jitter: 0.2 };

/**
 * Exponential backoff with jitter: 1s, 2s, 4s … capped at maxMs.
 * attempt is 1 for the first retry.
 */
export function backoffDelay(attempt: number, options: BackoffOptions = defaultBackoff, random: () => number = Math.random): number {
  const exponent = Math.max(0, attempt - 1);
  const base = Math.min(options.maxMs, options.initialMs * 2 ** exponent);
  const spread = base * options.jitter;
  const jittered = base - spread + random() * 2 * spread;
  return Math.round(Math.min(options.maxMs, Math.max(0, jittered)));
}
