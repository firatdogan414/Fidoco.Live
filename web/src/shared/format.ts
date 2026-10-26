const locale = 'sv-SE';
const timeZone = 'Europe/Stockholm';

/** 19900 öre → "199 kr" (or "199,50 kr"). */
export function formatMoney(minorUnits: number, currency = 'SEK'): string {
  const amount = minorUnits / 100;
  const formatted = new Intl.NumberFormat(locale, {
    minimumFractionDigits: Number.isInteger(amount) ? 0 : 2,
    maximumFractionDigits: 2,
  }).format(amount);
  return currency === 'SEK' ? `${formatted} kr` : `${formatted} ${currency}`;
}

/** "lördag 14 november 18:00" in Swedish time. */
export function formatEventDate(iso: string): string {
  return new Intl.DateTimeFormat(locale, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    hour: '2-digit',
    minute: '2-digit',
    timeZone,
  }).format(new Date(iso));
}

export function formatShortDate(iso: string): string {
  return new Intl.DateTimeFormat(locale, { dateStyle: 'short', timeStyle: 'short', timeZone }).format(new Date(iso));
}

export interface CountdownParts {
  days: number;
  hours: number;
  minutes: number;
  seconds: number;
  done: boolean;
}

/** Time left until <paramref name="target"/>; all zero and done when it has passed. */
export function countdown(nowMs: number, targetIso: string): CountdownParts {
  const remaining = Math.max(0, Math.floor((new Date(targetIso).getTime() - nowMs) / 1000));
  return {
    days: Math.floor(remaining / 86_400),
    hours: Math.floor((remaining % 86_400) / 3_600),
    minutes: Math.floor((remaining % 3_600) / 60),
    seconds: remaining % 60,
    done: remaining === 0,
  };
}
