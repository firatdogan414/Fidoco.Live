import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { platformApi } from '../../api/platform';
import { ErrorMessage, Loading } from '../../shared/components';
import { useErrorText } from '../../shared/errors';
import { formatMoney } from '../../shared/format';

function isoDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/** Settlements across all tenants for a period: what each organizer invoices Fidoco. */
export function SettlementsPage() {
  const { t } = useTranslation('platform');
  const errorText = useErrorText();
  const [from, setFrom] = useState(() => isoDate(new Date(Date.now() - 30 * 86_400_000)));
  const [to, setTo] = useState(() => isoDate(new Date(Date.now() + 86_400_000)));
  const settlements = useQuery({ queryKey: ['platform-settlements', from, to], queryFn: () => platformApi.settlements(from, to) });

  const rows = settlements.data ?? [];
  const totals = rows.reduce(
    (sum, r) => ({ gross: sum.gross + r.settlement.grossAmount, fee: sum.fee + r.settlement.platformFee, invoice: sum.invoice + r.settlement.organizerInvoiceTotal }),
    { gross: 0, fee: 0, invoice: 0 },
  );

  return (
    <section>
      <h1>{t('settlements.title')}</h1>
      <div className="copy-row form">
        <label>
          {t('settlements.from')}
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label>
          {t('settlements.to')}
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
      </div>
      {settlements.isPending ? <Loading /> : null}
      {settlements.isError ? <ErrorMessage>{errorText(settlements.error)}</ErrorMessage> : null}
      {settlements.data?.length === 0 ? <p className="muted">{t('settlements.empty')}</p> : null}
      {rows.length > 0 ? (
        <div className="table-scroll">
          <table className="settlement">
            <thead>
              <tr>
                <th>{t('settlements.tenant')}</th>
                <th>{t('settlements.event')}</th>
                <td>{t('settlements.sold')}</td>
                <td>{t('settlements.gross')}</td>
                <td>{t('settlements.fee')}</td>
                <td>{t('settlements.invoice')}</td>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={`${r.tenantId}-${r.settlement.eventId}`}>
                  <th>{r.tenantName}</th>
                  <th>{r.settlement.eventTitle}</th>
                  <td>{r.settlement.ticketsSold}</td>
                  <td>{formatMoney(r.settlement.grossAmount)}</td>
                  <td>{formatMoney(r.settlement.platformFee)}</td>
                  <td>{formatMoney(r.settlement.organizerInvoiceTotal)}</td>
                </tr>
              ))}
              <tr className="total">
                <th>{t('settlements.total')}</th>
                <th />
                <td />
                <td>{formatMoney(totals.gross)}</td>
                <td>{formatMoney(totals.fee)}</td>
                <td>{formatMoney(totals.invoice)}</td>
              </tr>
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}
