import { describe, expect, it } from 'vitest';
import common from './locales/sv/common.json';

// Every error code the API can return should have a Swedish text (errors.<code>).
const apiErrorCodes = [
  'unknown_tenant', 'event_not_found', 'ticket_not_found', 'event_not_published', 'event_ended', 'ticket_inactive',
  'sales_not_started', 'sales_ended', 'already_purchased', 'payment_provider_unavailable', 'invalid_or_expired_link',
  'email_unavailable', 'invalid_credentials', 'missing_client_header', 'no_ticket', 'not_started', 'live_not_included',
  'no_replay', 'replay_not_ready', 'access_expired', 'session_replaced', 'session_unknown', 'slug_taken',
  'event_not_draft', 'event_not_running', 'stream_exists', 'stream_not_found', 'video_provider_unavailable',
  'recording_not_ready', 'validation_failed', 'rate_limited', 'capacity_not_supported', 'admin_exists',
  'password_too_short', 'tenant_not_found', 'invalid_color', 'invalid_host', 'domain_taken', 'domain_not_found', 'last_domain',
  'file_too_large', 'unsupported_image', 'confirmation_mismatch',
];

describe('translations', () => {
  it('has a text for every API error code', () => {
    const missing = apiErrorCodes.filter((code) => !(code in common.errors));
    expect(missing).toEqual([]);
  });
});
