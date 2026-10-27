import { describe, expect, it, vi } from 'vitest';
import { ApiError, createApiClient } from './client';

function jsonResponse(status: number, body: unknown): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('api client', () => {
  it('sends JSON with the client header and parses the response', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, { name: 'AFN' }));
    const request = createApiClient(fetchMock);

    const result = await request<{ name: string }>('/tenant', { method: 'POST', body: { a: 1 } });

    expect(result).toEqual({ name: 'AFN' });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/v1/tenant');
    expect(init.method).toBe('POST');
    expect(init.headers['X-Fidoco-Client']).toBe('web');
    expect(init.headers['Content-Type']).toBe('application/json');
    expect(init.body).toBe('{"a":1}');
    expect(init.credentials).toBe('same-origin');
  });

  it('maps ProblemDetails to ApiError with the code', async () => {
    const request = createApiClient(vi.fn().mockResolvedValue(jsonResponse(409, { title: 'session_replaced', code: 'session_replaced' })));

    const error = await request('/x').catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(409);
    expect((error as ApiError).code).toBe('session_replaced');
    expect((error as ApiError).isTransient).toBe(false);
  });

  it('keeps validation field errors', async () => {
    const request = createApiClient(
      vi.fn().mockResolvedValue(jsonResponse(400, { code: 'validation_failed', errors: { title: ['required'] } })),
    );

    const error = (await request('/x').catch((e: unknown) => e)) as ApiError;

    expect(error.fieldErrors).toEqual({ title: ['required'] });
  });

  it('turns network failures into a transient error', async () => {
    const request = createApiClient(vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));

    const error = (await request('/x').catch((e: unknown) => e)) as ApiError;

    expect(error.code).toBe('network_error');
    expect(error.isTransient).toBe(true);
  });

  it('treats 5xx and 429 without a body as transient', async () => {
    const request = createApiClient(vi.fn().mockResolvedValue(new Response('oops', { status: 503 })));

    const error = (await request('/x').catch((e: unknown) => e)) as ApiError;

    expect(error.code).toBe('server_error');
    expect(error.isTransient).toBe(true);
  });

  it('returns undefined for 202 and 204', async () => {
    const request = createApiClient(vi.fn().mockResolvedValue(new Response(null, { status: 202 })));

    expect(await request('/auth/magic-link', { method: 'POST', body: {} })).toBeUndefined();
  });
});
