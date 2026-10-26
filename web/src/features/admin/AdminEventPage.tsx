import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { adminApi, type AdminEvent, type EventInput } from '../../api/admin';
import { ErrorMessage, Loading, Notice } from '../../shared/components';
import { useErrorText } from '../../shared/errors';
import { formatMoney } from '../../shared/format';
import { EventForm } from './EventForm';
import { SettlementPanel } from './SettlementPanel';
import { StreamPanel } from './StreamPanel';

export function AdminEventPage() {
  const { id = '' } = useParams();
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const queryClient = useQueryClient();
  const event = useQuery({ queryKey: ['admin-event', id], queryFn: () => adminApi.event(id), refetchInterval: 10_000 });

  const onChanged = async (ev?: AdminEvent) => {
    if (ev) queryClient.setQueryData(['admin-event', id], ev);
    await queryClient.invalidateQueries({ queryKey: ['admin-events'] });
  };

  const update = useMutation({ mutationFn: (input: EventInput) => adminApi.updateEvent(id, input), onSuccess: onChanged });
  const publish = useMutation({ mutationFn: () => adminApi.publish(id), onSuccess: onChanged });
  const end = useMutation({ mutationFn: () => adminApi.end(id), onSuccess: onChanged });
  const reloadEvent = useCallback(() => void queryClient.invalidateQueries({ queryKey: ['admin-event', id] }), [queryClient, id]);

  if (event.isPending) return <Loading />;
  if (event.isError) return <ErrorMessage>{errorText(event.error)}</ErrorMessage>;
  const ev = event.data;

  return (
    <section className="admin-event">
      <div className="row-between">
        <div>
          <span className={`badge badge-${ev.status}`}>{t(`status.${ev.status}`)}</span>
          <h1>{ev.title}</h1>
        </div>
        {ev.status !== 'draft' ? (
          <Link to={`/e/${ev.slug}`} className="button">
            {t('detail.publicPage')}
          </Link>
        ) : null}
      </div>

      {ev.status === 'draft' ? (
        <div className="panel">
          <p className="small muted">{t('detail.publishHelp')}</p>
          <button type="button" className="button button-primary" disabled={publish.isPending} onClick={() => publish.mutate()}>
            {t('detail.publish')}
          </button>
          {publish.isError ? <ErrorMessage>{errorText(publish.error)}</ErrorMessage> : null}
        </div>
      ) : null}

      <Stats event={ev} />
      <StreamPanel event={ev} onChanged={reloadEvent} />
      {ev.status !== 'draft' ? <SettlementPanel eventId={ev.id} /> : null}

      <div className="panel">
        <h2>{t('form.editTitle')}</h2>
        {update.isSuccess ? <Notice>{t('form.saved')}</Notice> : null}
        <EventForm key={ev.id} initial={ev} submitting={update.isPending} error={update.error} onSubmit={(input) => update.mutate(input)} />
      </div>

      {ev.status === 'scheduled' || ev.status === 'live' ? (
        <div className="panel">
          <button
            type="button"
            className="button button-danger"
            disabled={end.isPending}
            onClick={() => {
              if (window.confirm(t('detail.endConfirm'))) end.mutate();
            }}
          >
            {t('detail.end')}
          </button>
          {end.isError ? <ErrorMessage>{errorText(end.error)}</ErrorMessage> : null}
        </div>
      ) : null}
    </section>
  );
}

function Stats({ event }: { event: AdminEvent }) {
  const { t } = useTranslation('admin');
  const sales = useQuery({ queryKey: ['admin-sales', event.id], queryFn: () => adminApi.eventSales(event.id), refetchInterval: 30_000 });
  const viewers = useQuery({
    queryKey: ['admin-viewers', event.id],
    queryFn: () => adminApi.viewers(event.id),
    refetchInterval: event.status === 'live' || event.status === 'ended' ? 10_000 : false,
  });

  return (
    <div className="panel">
      <h2>{t('detail.stats')}</h2>
      <dl className="stats">
        <div>
          <dt>{t('detail.ticketsSold')}</dt>
          <dd>{sales.data?.ticketsSold ?? '–'}</dd>
        </div>
        <div>
          <dt>{t('detail.revenue')}</dt>
          <dd>{sales.data ? formatMoney(sales.data.revenue, sales.data.currency) : '–'}</dd>
        </div>
        <div>
          <dt>{t('detail.refunded')}</dt>
          <dd>{sales.data?.ticketsRefunded ?? '–'}</dd>
        </div>
        <div>
          <dt>{t('detail.viewers')}</dt>
          <dd>{viewers.data?.activeViewers ?? '–'}</dd>
        </div>
      </dl>
    </div>
  );
}
