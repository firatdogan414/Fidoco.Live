import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/client';
import type { PlaybackSession } from '../../../api/types';
import type { PlaybackApi } from '../../../api/viewer';
import { PlaybackSessionController, type PlaybackState } from './PlaybackSessionController';

const session: PlaybackSession = {
  sessionId: 's1',
  mode: 'live',
  playbackId: 'pb',
  tokens: { playback: 'v', thumbnail: 't', storyboard: 's' },
  tokenExpiresAt: '2026-11-14T20:15:00Z',
  heartbeatSeconds: 30,
};

const noJitter = { initialMs: 1000, maxMs: 30_000, jitter: 0 };

function setup(api: Partial<PlaybackApi> = {}) {
  const fullApi: PlaybackApi = {
    start: vi.fn().mockResolvedValue(session),
    heartbeat: vi.fn().mockResolvedValue(undefined),
    refreshTokens: vi.fn().mockResolvedValue({ playbackId: 'pb', tokens: { playback: 'v2', thumbnail: 't2', storyboard: 's2' }, tokenExpiresAt: 'x' }),
    end: vi.fn().mockResolvedValue(undefined),
    ...api,
  };
  const states: PlaybackState[] = [];
  const controller = new PlaybackSessionController(fullApi, 'event-1', (s) => states.push(s), { backoff: noJitter });
  return { api: fullApi, controller, states };
}

describe('PlaybackSessionController', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('starts a session and sends a heartbeat every 30 seconds', async () => {
    const { api, controller } = setup();

    await controller.start();
    expect(controller.current.kind).toBe('playing');

    await vi.advanceTimersByTimeAsync(29_000);
    expect(api.heartbeat).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1_000);
    expect(api.heartbeat).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(30_000);
    expect(api.heartbeat).toHaveBeenCalledTimes(2);
  });

  it('keeps playing and retries with backoff when heartbeats fail transiently', async () => {
    const heartbeat = vi
      .fn()
      .mockRejectedValueOnce(new ApiError(0, 'network_error'))
      .mockRejectedValueOnce(new ApiError(503, 'server_error'))
      .mockResolvedValue(undefined);
    const { controller } = setup({ heartbeat });

    await controller.start();
    await vi.advanceTimersByTimeAsync(30_000); // first heartbeat fails
    expect(controller.current).toMatchObject({ kind: 'playing', failedHeartbeats: 1 });

    await vi.advanceTimersByTimeAsync(1_000); // retry after 1 s fails
    expect(controller.current).toMatchObject({ kind: 'playing', failedHeartbeats: 2 });

    await vi.advanceTimersByTimeAsync(2_000); // retry after 2 s succeeds
    expect(heartbeat).toHaveBeenCalledTimes(3);
    expect(controller.current).toMatchObject({ kind: 'playing', failedHeartbeats: 0 });
  });

  it('stops with a clear state when the session is replaced by another device', async () => {
    const { api, controller } = setup({ heartbeat: vi.fn().mockRejectedValue(new ApiError(409, 'session_replaced')) });

    await controller.start();
    await vi.advanceTimersByTimeAsync(30_000);

    expect(controller.current.kind).toBe('replaced');
    await vi.advanceTimersByTimeAsync(120_000);
    expect(api.heartbeat).toHaveBeenCalledTimes(1);
  });

  it('stops when access ends or the user is signed out', async () => {
    const denied = setup({ heartbeat: vi.fn().mockRejectedValue(new ApiError(403, 'access_expired')) });
    await denied.controller.start();
    await vi.advanceTimersByTimeAsync(30_000);
    expect(denied.controller.current).toEqual({ kind: 'denied', reason: 'access_expired' });

    const signedOut = setup({ start: vi.fn().mockRejectedValue(new ApiError(401, 'unauthorized')) });
    await signedOut.controller.start();
    expect(signedOut.controller.current.kind).toBe('signedOut');
  });

  it('retries starting with backoff on transient errors', async () => {
    const start = vi.fn().mockRejectedValueOnce(new ApiError(0, 'network_error')).mockResolvedValue(session);
    const { controller } = setup({ start });

    await controller.start();
    expect(controller.current).toEqual({ kind: 'retrying', attempt: 1 });

    await vi.advanceTimersByTimeAsync(1_000);
    expect(controller.current.kind).toBe('playing');
  });

  it('starts a new session when the server no longer knows the old one', async () => {
    const start = vi.fn().mockResolvedValue(session);
    const { controller } = setup({ start, heartbeat: vi.fn().mockRejectedValueOnce(new ApiError(404, 'session_unknown')).mockResolvedValue(undefined) });

    await controller.start();
    await vi.advanceTimersByTimeAsync(30_000);

    expect(start).toHaveBeenCalledTimes(2);
    expect(controller.current.kind).toBe('playing');
  });

  it('refreshes tokens and ends the session on stop', async () => {
    const { api, controller } = setup();
    await controller.start();

    await controller.refreshTokens();
    expect(controller.current).toMatchObject({ kind: 'playing', tokens: { playback: 'v2' } });

    controller.stop();
    expect(api.end).toHaveBeenCalledWith('s1');
    await vi.advanceTimersByTimeAsync(60_000);
    expect(api.heartbeat).not.toHaveBeenCalled();
  });
});
