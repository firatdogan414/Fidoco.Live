import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { adminApi } from '../../api/admin';
import { ErrorMessage, Loading } from '../../shared/components';
import { useErrorText } from '../../shared/errors';
import { formatMoney, formatShortDate } from '../../shared/format';

export function AdminEventsPage() {
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const events = useQuery({ queryKey: ['admin-events'], queryFn: adminApi.events });
  const sales = useQuery({ queryKey: ['admin-sales'], queryFn: adminApi.sales });

  if (events.isPending) return <Loading />;
  if (events.isError) return <ErrorMessage>{errorText(events.error)}</ErrorMessage>;

  const salesFor = (eventId: string) => sales.data?.find((s) => s.eventId === eventId);

  return (
    <section>
      <div className="row-between">
        <h1>{t('events.title')}</h1>
        <Link to="/admin/event/ny" className="button button-primary">
          {t('events.new')}
        </Link>
      </div>
      {events.data.length === 0 ? <p className="muted">{t('events.empty')}</p> : null}
      <ul className="admin-list">
        {events.data.map((ev) => {
          const s = salesFor(ev.id);
          return (
            <li key={ev.id}>
              <Link to={`/admin/event/${ev.id}`} className="admin-list-item">
                <div>
                  <span className={`badge badge-${ev.status}`}>{t(`status.${ev.status}`)}</span>
                  <h2>{ev.title}</h2>
                  <p className="small muted">
                    {t('events.starts')}: {formatShortDate(ev.startsAt)}
                  </p>
                </div>
                <dl className="mini-stats">
                  <div>
                    <dt>{t('events.sold')}</dt>
                    <dd>{s?.ticketsSold ?? 0}</dd>
                  </div>
                  <div>
                    <dt>{t('events.revenue')}</dt>
                    <dd>{formatMoney(s?.revenue ?? 0, s?.currency)}</dd>
                  </div>
                </dl>
              </Link>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
