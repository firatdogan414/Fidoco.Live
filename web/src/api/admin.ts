import { request } from './client';
import type { EventStatus } from './types';

export interface AdminTicket {
  id: string;
  name: string;
  priceAmount: number;
  currency: string;
  vatRatePercent: number;
  includesLive: boolean;
  replayAccessDays: number | null;
}

export interface AdminStreamSummary {
  id: string;
  name: string;
  status: string;
}

export interface AdminEvent {
  id: string;
  slug: string;
  title: string;
  description: string | null;
  imageUrl: string | null;
  startsAt: string;
  status: EventStatus;
  liveStartedAt: string | null;
  endedAt: string | null;
  ticket: AdminTicket | null;
  stream: AdminStreamSummary | null;
}

export interface EventInput {
  title: string;
  slug?: string;
  description?: string;
  imageUrl?: string;
  startsAt: string;
  ticket: { name: string; priceAmount: number; replayAccessDays: number | null };
}

export interface Recording {
  id: string;
  status: string;
  durationSeconds: number | null;
  isPrimary: boolean;
  createdAt: string;
}

export interface StreamDetails {
  id: string;
  eventId: string;
  name: string;
  status: string;
  rtmpUrl: string;
  rtmpsUrl: string;
  streamKey: string;
  latencyMode: string;
  reconnectWindowSeconds: number;
  recordings: Recording[];
}

export interface StreamStatus {
  status: 'idle' | 'active' | 'disabled' | string;
  source: 'provider' | 'stored';
}

export interface EventSales {
  eventId: string;
  ticketsSold: number;
  ticketsRefunded: number;
  revenue: number;
  currency: string;
}

export interface Settlement {
  eventId: string;
  eventTitle: string;
  ticketsSold: number;
  ticketsRefunded: number;
  grossAmount: number;
  vatAmount: number;
  netAmount: number;
  platformFee: number;
  organizerShare: number;
  organizerVat: number;
  organizerInvoiceTotal: number;
  currency: string;
  organizerVatRegistered: boolean;
}

export interface AdminMe {
  email: string;
  role: string;
}

export const adminApi = {
  login: (email: string, password: string) => request<AdminMe>('/admin/auth/login', { method: 'POST', body: { email, password } }),
  logout: () => request<void>('/admin/auth/logout', { method: 'POST' }),
  me: () => request<AdminMe>('/admin/me'),

  events: () => request<AdminEvent[]>('/admin/events'),
  event: (id: string) => request<AdminEvent>(`/admin/events/${id}`),
  createEvent: (input: EventInput) => request<AdminEvent>('/admin/events', { method: 'POST', body: input }),
  updateEvent: (id: string, input: EventInput) => request<AdminEvent>(`/admin/events/${id}`, { method: 'PUT', body: input }),
  publish: (id: string) => request<AdminEvent>(`/admin/events/${id}/publish`, { method: 'POST' }),
  end: (id: string) => request<AdminEvent>(`/admin/events/${id}/end`, { method: 'POST' }),

  createStream: (eventId: string) => request<StreamDetails>(`/admin/events/${eventId}/streams`, { method: 'POST' }),
  stream: (id: string) => request<StreamDetails>(`/admin/streams/${id}`),
  streamStatus: (id: string) => request<StreamStatus>(`/admin/streams/${id}/status`),
  resetStreamKey: (id: string) => request<{ streamKey: string }>(`/admin/streams/${id}/reset-key`, { method: 'POST' }),
  selectPrimaryRecording: (id: string, recordingId: string) =>
    request<void>(`/admin/streams/${id}/primary-recording`, { method: 'PUT', body: { recordingId } }),
  /** Development only: simulate the encoder (fake video provider). */
  simulateStream: (id: string, status: 'active' | 'idle') => request<void>(`/dev/streams/${id}/simulate`, { method: 'POST', body: { status } }),

  sales: () => request<EventSales[]>('/admin/sales'),
  eventSales: (eventId: string) => request<EventSales>(`/admin/events/${eventId}/sales`),
  settlement: (eventId: string) => request<Settlement>(`/admin/events/${eventId}/settlement`),
  settlementCsvUrl: (eventId: string) => `/api/v1/admin/events/${eventId}/settlement?format=csv`,
  viewers: (eventId: string) => request<{ eventId: string; activeViewers: number }>(`/admin/events/${eventId}/viewers`),
};
