export type EventStatus = 'draft' | 'scheduled' | 'live' | 'ended';

export interface TenantBranding {
  name: string;
  slug: string;
  logoUrl: string | null;
  primaryColor: string;
  accentColor: string;
  supportEmail: string | null;
}

export interface TenantResponse {
  kind: 'tenant' | 'platform';
  tenant: TenantBranding | null;
}

export interface EventListItem {
  id: string;
  slug: string;
  title: string;
  imageUrl: string | null;
  startsAt: string;
  status: EventStatus;
}

export interface TicketType {
  id: string;
  name: string;
  priceAmount: number;
  currency: string;
  includesLive: boolean;
  replayAccessDays: number | null;
  onSale: boolean;
  notOnSaleReason: string | null;
}

export interface EventDetail {
  id: string;
  slug: string;
  title: string;
  description: string | null;
  imageUrl: string | null;
  startsAt: string;
  status: EventStatus;
  endedAt: string | null;
  ticketTypes: TicketType[];
}

export interface EventStatusResponse {
  id: string;
  status: EventStatus;
  startsAt: string;
  endedAt: string | null;
  streamActive: boolean;
  replayAvailable: boolean;
}

export interface AccessResponse {
  hasTicket: boolean;
  mode: 'live' | 'replay' | null;
  reason: string | null;
}

export interface Me {
  email: string;
}

/** "purchase": signed in right after paying, may only watch that purchase (no account access). */
export interface ViewerSession {
  scope: 'account' | 'purchase';
}

export interface MyPurchase {
  purchaseId: string;
  eventId: string;
  eventTitle: string;
  ticketTypeName: string;
  amount: number;
  currency: string;
  completedAt: string;
  status: 'completed' | 'refunded';
}

export interface CheckoutStarted {
  checkoutId: string;
  redirectUrl: string;
}

export interface CheckoutStatus {
  checkoutId: string;
  status: 'pending' | 'completed' | 'expired' | 'failed';
  eventId: string;
  eventSlug: string;
  signedIn: boolean;
}

export interface PlaybackTokens {
  playback: string;
  thumbnail: string;
  storyboard: string;
}

export interface PlaybackSession {
  sessionId: string;
  mode: 'live' | 'replay';
  playbackId: string;
  tokens: PlaybackTokens;
  tokenExpiresAt: string;
  heartbeatSeconds: number;
}

export interface TokenRefresh {
  playbackId: string;
  tokens: PlaybackTokens;
  tokenExpiresAt: string;
}
