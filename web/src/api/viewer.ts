import { request } from './client';
import type {
  AccessResponse,
  CheckoutStarted,
  CheckoutStatus,
  EventDetail,
  EventListItem,
  EventStatusResponse,
  Me,
  MyPurchase,
  PlaybackSession,
  TenantResponse,
  TokenRefresh,
} from './types';

export const viewerApi = {
  tenant: () => request<TenantResponse>('/tenant'),
  events: () => request<EventListItem[]>('/events'),
  event: (slug: string) => request<EventDetail>(`/events/${encodeURIComponent(slug)}`),
  eventStatus: (slug: string) => request<EventStatusResponse>(`/events/${encodeURIComponent(slug)}/status`),
  access: (eventId: string) => request<AccessResponse>(`/events/${eventId}/access`),

  requestLoginLink: (email: string, returnPath?: string) =>
    request<void>('/auth/magic-link', { method: 'POST', body: { email, returnPath } }),
  verify: (token: string) => request<{ returnPath: string }>('/auth/verify', { method: 'POST', body: { token } }),
  logout: () => request<void>('/auth/logout', { method: 'POST' }),
  me: () => request<Me>('/me'),
  deleteAccount: (confirmEmail: string) => request<void>('/me', { method: 'DELETE', body: { confirmEmail } }),
  myPurchases: () => request<MyPurchase[]>('/me/purchases'),

  startCheckout: (ticketTypeId: string) => request<CheckoutStarted>('/checkout', { method: 'POST', body: { ticketTypeId } }),
  checkout: (checkoutId: string) => request<CheckoutStatus>(`/checkout/${checkoutId}`),
};

/** The playback calls used by the session controller (an interface so tests can fake it). */
export interface PlaybackApi {
  start(eventId: string): Promise<PlaybackSession>;
  heartbeat(sessionId: string): Promise<void>;
  refreshTokens(sessionId: string): Promise<TokenRefresh>;
  end(sessionId: string): Promise<void>;
}

export const playbackApi: PlaybackApi = {
  start: (eventId) => request<PlaybackSession>(`/events/${eventId}/playback-sessions`, { method: 'POST' }),
  heartbeat: (sessionId) => request<void>(`/playback-sessions/${encodeURIComponent(sessionId)}/heartbeat`, { method: 'POST' }),
  refreshTokens: (sessionId) => request<TokenRefresh>(`/playback-sessions/${encodeURIComponent(sessionId)}/tokens`, { method: 'POST' }),
  end: (sessionId) => request<void>(`/playback-sessions/${encodeURIComponent(sessionId)}/end`, { method: 'POST', keepalive: true }),
};
