import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link, Navigate } from 'react-router';
import { viewerApi } from '../../../api/viewer';
import { ErrorMessage, Loading } from '../../../shared/components';
import { useErrorText } from '../../../shared/errors';
import { formatMoney, formatShortDate } from '../../../shared/format';
import { useMe } from '../useMe';

export function AccountPage() {
  const { t } = useTranslation('viewer');
  const errorText = useErrorText();
  const me = useMe();
  const purchases = useQuery({ queryKey: ['my-purchases'], queryFn: viewerApi.myPurchases, enabled: !!me.data });
  const events = useQuery({ queryKey: ['events'], queryFn: viewerApi.events, enabled: !!me.data });

  if (me.isPending) return <Loading />;
  if (!me.data) return <Navigate to="/logga-in?returnPath=/konto" replace />;

  const slugFor = (eventId: string) => events.data?.find((e) => e.id === eventId)?.slug;

  return (
    <section className="narrow">
      <h1>{t('account.title')}</h1>
      <p className="muted">{t('account.email', { email: me.data.email })}</p>
      <h2>{t('account.purchases')}</h2>
      {purchases.isError ? <ErrorMessage>{errorText(purchases.error)}</ErrorMessage> : null}
      {purchases.data?.length === 0 ? <p className="muted">{t('account.noPurchases')}</p> : null}
      <ul className="purchase-list">
        {purchases.data?.map((p) => {
          const slug = slugFor(p.eventId);
          return (
            <li key={`${p.purchaseId}-${p.eventId}`}>
              <div>
                <strong>{p.eventTitle}</strong>
                <div className="small muted">
                  {p.ticketTypeName} · {formatMoney(p.amount, p.currency)} · {formatShortDate(p.completedAt)}
                </div>
              </div>
              {p.status === 'refunded' ? (
                <span className="badge">{t('account.refunded')}</span>
              ) : slug ? (
                <Link to={`/e/${slug}/titta`} className="button">
                  {t('account.watch')}
                </Link>
              ) : null}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
