import { request } from './client';

export interface PlatformDomain {
  id: string;
  host: string;
  isPrimary: boolean;
}

export interface PlatformTenant {
  id: string;
  name: string;
  slug: string;
  logoUrl: string | null;
  primaryColor: string;
  accentColor: string;
  supportEmail: string | null;
  status: 'active' | 'suspended';
  commissionPercent: number;
  commissionFixedAmountPerTicket: number;
  organizerVatRegistered: boolean;
  domains: PlatformDomain[];
}

export interface TenantSettlement {
  tenantId: string;
  tenantName: string;
  settlement: {
    eventId: string;
    eventTitle: string;
    ticketsSold: number;
    ticketsRefunded: number;
    grossAmount: number;
    vatAmount: number;
    netAmount: number;
    platformFee: number;
    organizerShare: number;
    organizerInvoiceTotal: number;
    currency: string;
  };
}

/** Password links work the same for tenant admins (/admin) and the platform owner (/platform). */
export type AdminArea = 'admin' | 'platform';

export const passwordApi = {
  requestReset: (area: AdminArea, email: string) => request<void>(`/${area}/auth/reset-request`, { method: 'POST', body: { email } }),
  setPassword: (area: AdminArea, token: string, password: string) =>
    request<void>(`/${area}/auth/set-password`, { method: 'POST', body: { token, password } }),
};

export const platformApi = {
  login: (email: string, password: string) => request<{ email: string }>('/platform/auth/login', { method: 'POST', body: { email, password } }),
  logout: () => request<void>('/platform/auth/logout', { method: 'POST' }),
  me: () => request<{ email: string }>('/platform/me'),
  tenants: () => request<PlatformTenant[]>('/platform/tenants'),
  tenant: (id: string) => request<PlatformTenant>(`/platform/tenants/${id}`),
  createTenant: (input: { name: string; slug: string; supportEmail?: string }) =>
    request<PlatformTenant>('/platform/tenants', { method: 'POST', body: input }),
  updateTenant: (id: string, input: { name: string; supportEmail: string | null; logoUrl: string | null; primaryColor: string; accentColor: string; suspended: boolean }) =>
    request<PlatformTenant>(`/platform/tenants/${id}`, { method: 'PUT', body: input }),
  updateTerms: (id: string, input: { commissionPercent: number; commissionFixedAmountPerTicket: number; organizerVatRegistered: boolean }) =>
    request<PlatformTenant>(`/platform/tenants/${id}/terms`, { method: 'PUT', body: input }),
  addDomain: (id: string, host: string, isPrimary: boolean) =>
    request<PlatformTenant>(`/platform/tenants/${id}/domains`, { method: 'POST', body: { host, isPrimary } }),
  removeDomain: (id: string, domainId: string) => request<PlatformTenant>(`/platform/tenants/${id}/domains/${domainId}`, { method: 'DELETE' }),
  inviteAdmin: (id: string, email: string) => request<void>(`/platform/tenants/${id}/admins`, { method: 'POST', body: { email } }),
  settlements: (from: string, to: string) =>
    request<TenantSettlement[]>(`/platform/settlements?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),
};
