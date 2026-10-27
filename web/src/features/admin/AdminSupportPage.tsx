import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { adminApi, type SupportLogEntry, type SupportTicket } from '../../api/admin';
import { ErrorMessage, Loading, Notice } from '../../shared/components';
import { useErrorText } from '../../shared/errors';
import { formatMoney, formatShortDate } from '../../shared/format';

/**
 * Support for buyers ("I paid but cannot get in"): find the buyer by e-mail, see purchases, tickets and
 * whether a device is watching right now, and send a new login link. Every action is logged.
 */
export function AdminSupportPage() {
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const [input, setInput] = useState('');
  const [query, setQuery] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const results = useQuery({
    queryKey: ['support-search', query],
    queryFn: () => adminApi.searchCustomers(query),
    enabled: query.length >= 3,
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    setQuery(input.trim());
    setSelectedId(null);
  };

  const found = results.data ?? [];
  const shownId = selectedId ?? (found.length === 1 ? found[0].id : null);

  return (
    <section>
      <h1>{t('support.title')}</h1>
      <form className="form panel" onSubmit={submit}>
        <p className="small muted">{t('support.help')}</p>
        <label>
          {t('support.searchLabel')}
          <input type="search" required minLength={3} value={input} onChange={(e) => setInput(e.target.value)} />
        </label>
        <button type="submit" className="button button-primary">
          {t('support.search')}
        </button>
      </form>

      {results.isFetching && !results.data ? <Loading /> : null}
      {results.isError ? <ErrorMessage>{errorText(results.error)}</ErrorMessage> : null}
      {results.isSuccess && found.length === 0 ? <p className="muted">{t('support.noResults')}</p> : null}
      {found.length > 1 ? (
        <ul className="purchase-list">
          {found.map((c) => (
            <li key={c.id}>
              <button type="button" className="link-button" onClick={() => setSelectedId(c.id)}>
                {c.email}
              </button>
              <span className="small muted">{c.id === shownId ? t('support.selected') : ''}</span>
            </li>
          ))}
        </ul>
      ) : null}

      {shownId ? <CustomerPanel key={shownId} customerId={shownId} /> : null}
      <SupportLog />
    </section>
  );
}

function CustomerPanel({ customerId }: { customerId: string }) {
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const queryClient = useQueryClient();
  // Refreshed so "watching now" follows the buyer while support is on the phone with them.
  const details = useQuery({
    queryKey: ['support-customer', customerId],
    queryFn: () => adminApi.customerDetails(customerId),
    refetchInterval: 15_000,
  });
  const sendLink = useMutation({
    mutationFn: (eventId: string | null) => adminApi.sendLoginLink(customerId, eventId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['support-customer', customerId] });
      void queryClient.invalidateQueries({ queryKey: ['support-log'] });
    },
  });

  if (details.isPending) return <Loading />;
  if (details.isError) return <ErrorMessage>{errorText(details.error)}</ErrorMessage>;
  const { customer, purchases, tickets, log } = details.data;

  return (
    <div className="panel">
      <div className="row-between">
        <div>
          <h2>{customer.email}</h2>
          <p className="small muted">
            {t('support.customerSince')} {formatShortDate(customer.createdAt)} ·{' '}
            {customer.lastLoginAt ? `${t('support.lastLogin')} ${formatShortDate(customer.lastLoginAt)}` : t('support.neverLoggedIn')}
          </p>
        </div>
        <button type="button" className="button" disabled={sendLink.isPending} onClick={() => sendLink.mutate(null)}>
          {t('support.sendLink')}
        </button>
      </div>
      {sendLink.isSuccess ? <Notice>{t('support.linkSent', { email: customer.email })}</Notice> : null}
      {sendLink.isError ? <ErrorMessage>{errorText(sendLink.error)}</ErrorMessage> : null}

      <h3>{t('support.tickets')}</h3>
      {tickets.length === 0 ? <p className="muted small">{t('support.noTickets')}</p> : null}
      <ul className="purchase-list">
        {tickets.map((ticket) => (
          <li key={ticket.entitlementId}>
            <div>
              <strong>{ticket.eventTitle}</strong>{' '}
              <span className="badge">{t(ticket.source === 'comp' ? 'support.sourceComp' : 'support.sourcePurchase')}</span>{' '}
              {ticket.sessionActive ? <span className="badge badge-live">{t('support.watchingNow')}</span> : null}
              <div className="small muted">{accessText(ticket, t)}</div>
            </div>
            {!ticket.revoked ? (
              <button type="button" className="button" disabled={sendLink.isPending} onClick={() => sendLink.mutate(ticket.eventId)}>
                {t('support.sendLinkToEvent')}
              </button>
            ) : null}
          </li>
        ))}
      </ul>

      <h3>{t('support.purchases')}</h3>
      {purchases.length === 0 ? <p className="muted small">{t('support.noPurchases')}</p> : null}
      {purchases.length > 0 ? (
        <div className="table-scroll">
          <table className="settlement">
            <tbody>
              {purchases.map((p) => (
                <tr key={`${p.purchaseId}-${p.eventId}`}>
                  <th>
                    {p.eventTitle} – {p.ticketTypeName}
                    <div className="small muted">
                      {t('support.order')} {p.orderNumber} · {formatShortDate(p.completedAt)}
                    </div>
                  </th>
                  <td>{formatMoney(p.amountPaid, p.currency)}</td>
                  <td>{p.refunded ? t('support.refunded') : t('support.paid')}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}

      {log.length > 0 ? (
        <>
          <h3>{t('support.customerLog')}</h3>
          <LogList entries={log} showCustomer={false} />
        </>
      ) : null}
    </div>
  );
}

function accessText(ticket: SupportTicket, t: (key: string) => string): string {
  if (ticket.revoked) return t(ticket.source === 'comp' ? 'support.access.revokedComp' : 'support.access.revoked');
  if (ticket.accessMode) return t(`support.access.${ticket.accessMode}`);
  return t(`support.access.${ticket.denyReason ?? 'no_ticket'}`);
}

function SupportLog() {
  const { t } = useTranslation('admin');
  const log = useQuery({ queryKey: ['support-log'], queryFn: adminApi.supportLog, refetchInterval: 30_000 });
  if (!log.data || log.data.length === 0) return null;
  return (
    <div className="panel">
      <h2>{t('support.log')}</h2>
      <LogList entries={log.data} showCustomer />
    </div>
  );
}

function LogList({ entries, showCustomer }: { entries: SupportLogEntry[]; showCustomer: boolean }) {
  const { t } = useTranslation('admin');
  return (
    <ul className="purchase-list">
      {entries.map((e) => (
        <li key={e.id}>
          <div>
            <strong>{t(`support.actions.${e.action}`)}</strong>
            {showCustomer ? ` · ${e.customerEmail ?? t('support.erasedCustomer')}` : ''}
            {e.eventTitle ? ` · ${e.eventTitle}` : ''}
            {e.details ? <div className="small muted">{e.details}</div> : null}
          </div>
          <span className="small muted">
            {formatShortDate(e.createdAt)} · {e.adminEmail}
          </span>
        </li>
      ))}
    </ul>
  );
}
