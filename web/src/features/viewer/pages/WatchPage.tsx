import MuxPlayer from '@mux/mux-player-react';
import { useQuery } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, Navigate, useParams } from 'react-router';
import { ApiError } from '../../../api/client';
import type { EventStatusResponse } from '../../../api/types';
import { viewerApi } from '../../../api/viewer';
import { ErrorMessage, Loading, Notice } from '../../../shared/components';
import { useErrorText } from '../../../shared/errors';
import { countdown, formatEventDate } from '../../../shared/format';
import { useNow } from '../../../shared/hooks';
import type { PlaybackState } from '../playback/PlaybackSessionController';
import { usePlaybackSession } from '../playback/usePlaybackSession';
import { useViewerSession } from '../useViewerSession';

/** Status is polled every 15 s (cached server-side) so the page switches to live by itself. */
const statusPollMs = 15_000;

export function WatchPage() {
  const { slug = '' } = useParams();
  const { t } = useTranslation('viewer');
  const errorText = useErrorText();
  const session = useViewerSession();
  const event = useQuery({ queryKey: ['event', slug], queryFn: () => viewerApi.event(slug) });
  const status = useQuery({
    queryKey: ['event-status', slug],
    queryFn: () => viewerApi.eventStatus(slug),
    refetchInterval: statusPollMs,
    retry: 3,
  });

  const playable = isPlayable(status.data);
  const playback = usePlaybackSession(event.data?.id, playable && !!session.data);

  if (event.isPending || session.isPending) return <Loading />;
  if (event.isError) return <ErrorMessage>{errorText(event.error)}</ErrorMessage>;
  if (!session.data) return <Navigate to={`/logga-in?returnPath=${encodeURIComponent(`/e/${slug}/titta`)}`} replace />;

  const ev = event.data;
  const current = status.data;

  return (
    <section className="watch">
      <div className="player-frame">
        {!current || current.status === 'scheduled' ? (
          <Countdown startsAt={ev.startsAt} />
        ) : current.status === 'live' && !current.streamActive ? (
          <Overlay text={t('watch.waitingForSignal')} />
        ) : current.status === 'ended' && !current.replayAvailable ? (
          <Overlay text={`${t('watch.ended')} ${t('watch.replayComing')}`} />
        ) : (
          <PlaybackView state={playback.state} title={ev.title} onTakeOver={playback.takeOver} onPlayerError={playback.refreshTokens} slug={slug} />
        )}
      </div>
      <div className="watch-info">
        <h1>{ev.title}</h1>
        <p className="muted">{formatEventDate(ev.startsAt)}</p>
        {ev.description ? <p className="description">{ev.description}</p> : null}
      </div>
    </section>
  );
}

function isPlayable(status: EventStatusResponse | undefined): boolean {
  if (!status) return false;
  return (status.status === 'live' && status.streamActive) || (status.status === 'ended' && status.replayAvailable);
}

function PlaybackView({
  state,
  title,
  slug,
  onTakeOver,
  onPlayerError,
}: {
  state: PlaybackState;
  title: string;
  slug: string;
  onTakeOver: () => void;
  onPlayerError: () => void;
}) {
  const { t } = useTranslation('viewer');
  const errorText = useErrorText();

  switch (state.kind) {
    case 'idle':
    case 'starting':
      return <Overlay text={t('watch.connecting')} />;
    case 'retrying':
      return <Overlay text={t('watch.reconnecting')} />;
    case 'signedOut':
      return <Navigate to={`/logga-in?returnPath=${encodeURIComponent(`/e/${slug}/titta`)}`} replace />;
    case 'replaced':
      return (
        <Overlay text={t('watch.replaced')}>
          <p className="small">{t('watch.replacedHelp')}</p>
          <button type="button" className="button button-primary" onClick={onTakeOver}>
            {t('watch.watchHere')}
          </button>
        </Overlay>
      );
    case 'denied':
      return (
        <Overlay text={errorText(new ApiError(403, state.reason))}>
          {state.reason === 'no_ticket' ? (
            <Link to={`/e/${slug}`} className="button button-primary">
              {t('watch.buyTicket')}
            </Link>
          ) : null}
        </Overlay>
      );
    case 'playing': {
      const { session, tokens } = state;
      if (session.playbackId.startsWith('fake-')) {
        return <Overlay text={t('watch.devPlayer')} />;
      }
      return (
        <div className="player">
          <MuxPlayer
            playbackId={session.playbackId}
            tokens={{ playback: tokens.playback, thumbnail: tokens.thumbnail, storyboard: tokens.storyboard }}
            streamType={session.mode === 'live' ? 'live' : 'on-demand'}
            metadata={{ video_title: title }}
            disableCookies
            disableTracking
            autoPlay
            accentColor="var(--color-accent)"
            onError={onPlayerError}
          />
          <span className={`badge badge-${session.mode === 'live' ? 'live' : 'ended'} player-badge`}>
            {session.mode === 'live' ? t('watch.liveBadge') : t('watch.replayBadge')}
          </span>
          {state.failedHeartbeats > 2 ? <Notice>{t('watch.reconnecting')}</Notice> : null}
        </div>
      );
    }
  }
}

function Countdown({ startsAt }: { startsAt: string }) {
  const { t } = useTranslation('viewer');
  const now = useNow();
  const parts = countdown(now, startsAt);

  if (parts.done) return <Overlay text={t('watch.startingSoon')} />;

  return (
    <div className="overlay">
      <p>{t('watch.countdownTitle')}</p>
      <div className="countdown">
        {parts.days > 0 ? <CountdownUnit value={parts.days} label={t('watch.days')} /> : null}
        <CountdownUnit value={parts.hours} label={t('watch.hours')} />
        <CountdownUnit value={parts.minutes} label={t('watch.minutes')} />
        <CountdownUnit value={parts.seconds} label={t('watch.seconds')} />
      </div>
    </div>
  );
}

function CountdownUnit({ value, label }: { value: number; label: string }) {
  return (
    <div className="countdown-unit">
      <span className="countdown-value">{String(value).padStart(2, '0')}</span>
      <span className="countdown-label">{label}</span>
    </div>
  );
}

function Overlay({ text, children }: { text: string; children?: ReactNode }) {
  return (
    <div className="overlay">
      <p>{text}</p>
      {children}
    </div>
  );
}
