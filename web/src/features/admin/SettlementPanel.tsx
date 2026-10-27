import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { adminApi } from '../../api/admin';
import { ErrorMessage } from '../../shared/components';
import { useErrorText } from '../../shared/errors';
import { formatMoney } from '../../shared/format';

/** The organizer's settlement for an event: the basis for its invoice to Fidoco. */
export function SettlementPanel({ eventId }: { eventId: string }) {
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const settlement = useQuery({ queryKey: ['admin-settlement', eventId], queryFn: () => adminApi.settlement(eventId), refetchInterval: 60_000 });

  if (settlement.isError) return <ErrorMessage>{errorText(settlement.error)}</ErrorMessage>;
  const s = settlement.data;
  const money = (amount: number | undefined) => (amount === undefined ? '–' : formatMoney(amount, s?.currency));

  return (
    <div className="panel">
      <div className="row-between">
        <h2>{t('settlement.title')}</h2>
        <a className="button" href={adminApi.settlementCsvUrl(eventId)} download>
          {t('settlement.download')}
        </a>
      </div>
      <p className="small muted">{t('settlement.help')}</p>
      <table className="settlement">
        <tbody>
          <tr><th>{t('settlement.comps')}</th><td>{s ? t('settlement.compsValue', { count: s.complimentaryTickets }) : '–'}</td></tr>
          <tr><th>{t('settlement.discounts')}</th><td>{money(s?.discountAmount)}</td></tr>
          <tr><th>{t('settlement.gross')}</th><td>{money(s?.grossAmount)}</td></tr>
          <tr><th>{t('settlement.vat')}</th><td>{money(s?.vatAmount)}</td></tr>
          <tr><th>{t('settlement.net')}</th><td>{money(s?.netAmount)}</td></tr>
          <tr><th>{t('settlement.fee')}</th><td>− {money(s?.platformFee)}</td></tr>
          <tr><th>{t('settlement.share')}</th><td>{money(s?.organizerShare)}</td></tr>
          <tr>
            <th>{s && !s.organizerVatRegistered ? t('settlement.notVatRegistered') : t('settlement.organizerVat')}</th>
            <td>{money(s?.organizerVat)}</td>
          </tr>
          <tr className="total"><th>{t('settlement.invoiceTotal')}</th><td>{money(s?.organizerInvoiceTotal)}</td></tr>
        </tbody>
      </table>
    </div>
  );
}
