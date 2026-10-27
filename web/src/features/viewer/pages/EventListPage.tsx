import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { viewerApi } from '../../../api/viewer';
import { ErrorMessage, Loading } from '../../../shared/components';
import { useErrorText } from '../../../shared/errors';
import { formatEventDate } from '../../../shared/format';

export function EventListPage() {
  const { t } = useTranslation('viewer');
  const errorText = useErrorText();
  const events = useQuery({ queryKey: ['events'], queryFn: viewerApi.events });

  if (events.isPending) return <Loading />;
  if (events.isError) return <ErrorMessage>{errorText(events.error)}</ErrorMessage>;

  return (
    <section>
      <h1>{t('events.title')}</h1>
      {events.data.length === 0 ? <p className="muted">{t('events.empty')}</p> : null}
      <ul className="event-list">
        {events.data.map((ev) => (
          <li key={ev.id}>
            <Link to={`/e/${ev.slug}`} className="event-card">
              {ev.imageUrl ? <img src={ev.imageUrl} alt="" className="event-card-image" /> : null}
              <div className="event-card-body">
                <span className={`badge badge-${ev.status}`}>
                  {ev.status === 'live' ? t('events.live') : ev.status === 'ended' ? t('events.ended') : t('events.upcoming')}
                </span>
                <h2>{ev.title}</h2>
                <p className="muted">{formatEventDate(ev.startsAt)}</p>
              </div>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}
