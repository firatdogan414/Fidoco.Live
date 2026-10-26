/** Error from the API: HTTP status plus the machine-readable code from ProblemDetails. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, code: string, fieldErrors: Record<string, string[]> = {}) {
    super(`${status} ${code}`);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.fieldErrors = fieldErrors;
  }

  /** Network failures, 5xx and 429 are worth retrying; 4xx are answers. */
  get isTransient(): boolean {
    return this.status === 0 || this.status === 429 || this.status >= 500;
  }
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  body?: unknown;
  signal?: AbortSignal;
  /** Lets a request outlive the page (used when ending a playback session on unload). */
  keepalive?: boolean;
}

export type FetchLike = (input: string, init?: RequestInit) => Promise<Response>;

const baseUrl = '/api/v1';

/**
 * JSON over fetch with the auth cookies and the X-Fidoco-Client header the API requires on
 * state-changing calls. Throws ApiError with status 0 and code "network_error" when offline.
 */
export function createApiClient(fetchImpl: FetchLike = (input, init) => fetch(input, init)) {
  return async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json', 'X-Fidoco-Client': 'web' };
    if (options.body !== undefined) {
      headers['Content-Type'] = 'application/json';
    }

    let response: Response;
    try {
      response = await fetchImpl(baseUrl + path, {
        method: options.method ?? 'GET',
        headers,
        body: options.body === undefined ? undefined : JSON.stringify(options.body),
        credentials: 'same-origin',
        signal: options.signal,
        keepalive: options.keepalive,
      });
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') {
        throw error;
      }
      throw new ApiError(0, 'network_error');
    }

    if (!response.ok) {
      throw await toApiError(response);
    }

    if (response.status === 204 || response.status === 202) {
      return undefined as T;
    }

    const text = await response.text();
    return (text ? JSON.parse(text) : undefined) as T;
  };
}

async function toApiError(response: Response): Promise<ApiError> {
  try {
    const problem = (await response.json()) as { code?: string; title?: string; errors?: Record<string, string[]> };
    return new ApiError(response.status, problem.code ?? problem.title ?? fallbackCode(response.status), problem.errors ?? {});
  } catch {
    return new ApiError(response.status, fallbackCode(response.status));
  }
}

function fallbackCode(status: number): string {
  if (status === 401) return 'unauthorized';
  if (status === 403) return 'forbidden';
  if (status === 404) return 'not_found';
  if (status === 429) return 'rate_limited';
  return status >= 500 ? 'server_error' : 'request_failed';
}

export const request = createApiClient();

/** Multipart upload of one file (field "file"); returns the parsed JSON response. */
export async function uploadFile<T>(path: string, file: File, fetchImpl: FetchLike = (input, init) => fetch(input, init)): Promise<T> {
  const form = new FormData();
  form.append('file', file);
  let response: Response;
  try {
    response = await fetchImpl(baseUrl + path, {
      method: 'POST',
      body: form,
      headers: { Accept: 'application/json', 'X-Fidoco-Client': 'web' },
      credentials: 'same-origin',
    });
  } catch {
    throw new ApiError(0, 'network_error');
  }
  if (!response.ok) {
    throw await toApiError(response);
  }
  return (await response.json()) as T;
}
