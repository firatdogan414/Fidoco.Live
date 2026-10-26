import { ApiError } from '../../../api/client';
import type { PlaybackSession, PlaybackTokens } from '../../../api/types';
import type { PlaybackApi } from '../../../api/viewer';
import { backoffDelay, defaultBackoff, type BackoffOptions } from '../../../shared/backoff';

export type PlaybackState =
  | { kind: 'idle' }
  | { kind: 'starting' }
  | { kind: 'playing'; session: PlaybackSession; tokens: PlaybackTokens; failedHeartbeats: number }
  /** Another device started watching with the same ticket. */
  | { kind: 'replaced' }
  /** Access denied: no_ticket, not_started, access_expired, ... */
  | { kind: 'denied'; reason: string }
  | { kind: 'signedOut' }
  /** Could not start because of a transient error; retrying automatically. */
  | { kind: 'retrying'; attempt: number };

export interface Timers {
  setTimeout(callback: () => void, ms: number): unknown;
  clearTimeout(handle: unknown): void;
}

const browserTimers: Timers = {
  setTimeout: (callback, ms) => globalThis.setTimeout(callback, ms),
  clearTimeout: (handle) => globalThis.clearTimeout(handle as ReturnType<typeof setTimeout>),
};

export interface ControllerOptions {
  timers?: Timers;
  backoff?: BackoffOptions;
  random?: () => number;
}

/**
 * Keeps one playback session alive: starts it, sends heartbeats and refreshes tokens.
 * Transient failures (network, 5xx, 429) never stop playback: they are retried with exponential
 * backoff while the video keeps playing. Only answers from the server stop it:
 * 409 session_replaced, 403 (access ended) and 401 (signed out).
 */
export class PlaybackSessionController {
  private state: PlaybackState = { kind: 'idle' };
  private timer: unknown;
  private stopped = false;
  private readonly timers: Timers;
  private readonly backoff: BackoffOptions;
  private readonly random: () => number;

  constructor(
    private readonly api: PlaybackApi,
    private readonly eventId: string,
    private readonly onChange: (state: PlaybackState) => void,
    options: ControllerOptions = {},
  ) {
    this.timers = options.timers ?? browserTimers;
    this.backoff = options.backoff ?? defaultBackoff;
    this.random = options.random ?? Math.random;
  }

  get current(): PlaybackState {
    return this.state;
  }

  /** Starts (or restarts) a session. Also used for "watch here instead" after being replaced. */
  async start(attempt = 0): Promise<void> {
    this.stopped = false;
    this.clearTimer();
    this.set(attempt === 0 ? { kind: 'starting' } : { kind: 'retrying', attempt });
    try {
      const session = await this.api.start(this.eventId);
      if (this.stopped) {
        void this.api.end(session.sessionId).catch(() => undefined);
        return;
      }
      this.set({ kind: 'playing', session, tokens: session.tokens, failedHeartbeats: 0 });
      this.schedule(session.heartbeatSeconds * 1000, () => this.heartbeat());
    } catch (error) {
      if (this.stopped) return;
      if (this.handleTerminal(error)) return;
      const next = attempt + 1;
      this.set({ kind: 'retrying', attempt: next });
      this.schedule(backoffDelay(next, this.backoff, this.random), () => void this.start(next));
    }
  }

  /** New tokens when the player reports an auth error; retried with backoff on transient failures. */
  async refreshTokens(attempt = 0): Promise<void> {
    if (this.state.kind !== 'playing') return;
    const sessionId = this.state.session.sessionId;
    try {
      const refreshed = await this.api.refreshTokens(sessionId);
      if (this.state.kind === 'playing' && this.state.session.sessionId === sessionId) {
        this.set({
          ...this.state,
          session: { ...this.state.session, playbackId: refreshed.playbackId, tokenExpiresAt: refreshed.tokenExpiresAt },
          tokens: refreshed.tokens,
        });
      }
    } catch (error) {
      if (this.stopped || this.handleTerminal(error)) return;
      this.timers.setTimeout(() => void this.refreshTokens(attempt + 1), backoffDelay(attempt + 1, this.backoff, this.random));
    }
  }

  /** Stops heartbeats and frees the ticket (called when leaving the page). */
  stop(): void {
    this.stopped = true;
    this.clearTimer();
    if (this.state.kind === 'playing') {
      void this.api.end(this.state.session.sessionId).catch(() => undefined);
    }
  }

  private async heartbeat(): Promise<void> {
    if (this.state.kind !== 'playing' || this.stopped) return;
    const session = this.state.session;
    try {
      await this.api.heartbeat(session.sessionId);
      if (this.state.kind !== 'playing' || this.stopped) return;
      this.set({ ...this.state, failedHeartbeats: 0 });
      this.schedule(session.heartbeatSeconds * 1000, () => this.heartbeat());
    } catch (error) {
      if (this.stopped || this.state.kind !== 'playing') return;
      if (error instanceof ApiError && error.status === 404) {
        // The server no longer knows the session (e.g. its cache was cleared): start a new one.
        void this.start();
        return;
      }
      if (this.handleTerminal(error)) return;
      const failed = this.state.failedHeartbeats + 1;
      this.set({ ...this.state, failedHeartbeats: failed });
      const delay = Math.min(backoffDelay(failed, this.backoff, this.random), session.heartbeatSeconds * 1000);
      this.schedule(delay, () => this.heartbeat());
    }
  }

  /** Handles answers that end playback. Returns false for transient errors. */
  private handleTerminal(error: unknown): boolean {
    if (!(error instanceof ApiError) || error.isTransient) {
      return false;
    }
    this.clearTimer();
    if (error.status === 401) {
      this.set({ kind: 'signedOut' });
    } else if (error.status === 409 && error.code === 'session_replaced') {
      this.set({ kind: 'replaced' });
    } else {
      this.set({ kind: 'denied', reason: error.code });
    }
    return true;
  }

  private schedule(ms: number, callback: () => void): void {
    this.clearTimer();
    this.timer = this.timers.setTimeout(callback, ms);
  }

  private clearTimer(): void {
    if (this.timer !== undefined) {
      this.timers.clearTimeout(this.timer);
      this.timer = undefined;
    }
  }

  private set(state: PlaybackState): void {
    this.state = state;
    this.onChange(state);
  }
}
