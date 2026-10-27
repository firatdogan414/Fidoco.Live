import { useMutation, useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router';
import { ApiError } from '../../../api/client';
import type { TicketType } from '../../../api/types';
import { viewerApi } from '../../../api/viewer';
import { ErrorMessage, Loading } from '../../../shared/components';
import { useErrorText } from '../../../shared/errors';
import { formatEventDate, formatMoney } from '../../../shared/format';
import { useMe } from '../useMe';

export function EventPage() {
  const { slug = '' } = useParams();
  const { t } = useTranslation('viewer');
  const errorText = useErrorText();
  const navigate = useNavigate();
  const me = useMe();
  const event = useQuery({ queryKey: ['event', slug], queryFn: () => viewerApi.event(slug) });
  const access = useQuery({
    queryKey: ['access', event.data?.id],
    queryFn: () => viewerApi.access(event.data!.id),
    enabled: !!event.data && !!me.data,
  });

  const buy = useMutation({
    mutationFn: (ticketTypeId: string) => viewerApi.startCheckout(ticketTypeId),
    onSuccess: (started) => {
      window.location.assign(started.redirectUrl);
    },
    onError: (error) => {
      if (error instanceof ApiError && error.code === 'already_purchased') {
        void navigate(`/e/${slug}/titta`);
      }
    },
  });

  if (event.isPending) return <Loading />;
  if (event.isError) return <ErrorMessage>{errorText(event.error)}</ErrorMessage>;

  const ev = event.data;
  const hasTicket = access.data?.hasTicket ?? false;

  return (
    <article className="event">
      {ev.imageUrl ? <img src={ev.imageUrl} alt="" className="event-hero" /> : null}
      <div className="event-head">
        {ev.status === 'live' ? <span className="badge badge-live">{t('event.statusLive')}</span> : null}
        {ev.status === 'ended' ? <span className="badge badge-ended">{t('event.statusEnded')}</span> : null}
        <h1>{ev.title}</h1>
        <p className="muted">{formatEventDate(ev.startsAt)}</p>
      </div>

      {/* Slot for industry modules (e.g. a live fight card) in later phases. */}

      <div className="ticket-box">
        {hasTicket ? (
          <>
            <p>{t('event.haveTicket')}</p>
            <Link to={`/e/${ev.slug}/titta`} className="button button-primary">
              {ev.status === 'ended' ? t('event.watchReplay') : t('event.watch')}
            </Link>
          </>
        ) : (
          ev.ticketTypes.map((ticket) => (
            <TicketOffer key={ticket.id} ticket={ticket} busy={buy.isPending} onBuy={() => buy.mutate(ticket.id)} />
          ))
        )}
        {buy.isError && !(buy.error instanceof ApiError && buy.error.code === 'already_purchased') ? (
          <ErrorMessage>{errorText(buy.error)}</ErrorMessage>
        ) : null}
        {!me.data && !hasTicket ? (
          <p className="small">
            <Link to={`/logga-in?returnPath=${encodeURIComponent(`/e/${ev.slug}/titta`)}`}>{t('event.signInToWatch')}</Link>
          </p>
        ) : null}
      </div>

      {ev.description ? <p className="description">{ev.description}</p> : null}
    </article>
  );
}

function TicketOffer({ ticket, busy, onBuy }: { ticket: TicketType; busy: boolean; onBuy: () => void }) {
  const { t } = useTranslation('viewer');
  const errorText = useErrorText();
  const price = formatMoney(ticket.priceAmount, ticket.currency);

  return (
    <div className="ticket">
      <h2>{ticket.name}</h2>
      <ul className="ticket-features">
        {ticket.includesLive ? <li>{t('event.includesLive')}</li> : null}
        <li>
          {ticket.replayAccessDays ? t('event.replayDays', { count: ticket.replayAccessDays }) : t('event.noReplay')}
        </li>
        <li>{t('event.oneDevice')}</li>
      </ul>
      {ticket.onSale ? (
        <button type="button" className="button button-primary button-block" disabled={busy} onClick={onBuy}>
          {busy ? t('event.buying') : t('event.buy', { price })}
        </button>
      ) : (
        <p className="muted">{errorText(new ApiError(409, ticket.notOnSaleReason ?? 'ticket_inactive'))}</p>
      )}
    </div>
  );
}
