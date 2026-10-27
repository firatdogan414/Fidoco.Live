import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminApi, type AdminEvent } from '../../api/admin';
import { ErrorMessage, Loading } from '../../shared/components';
import { useErrorText } from '../../shared/errors';
import { formatShortDate } from '../../shared/format';

/** Encoder status is polled every 10 seconds while the admin page is open. */
const statusPollMs = 10_000;

/**
 * The event's stream: create it (Mux live stream), show the RTMP server and stream key to paste into
 * vMix/OBS, and show whether a signal is coming in.
 */
export function StreamPanel({ event, onChanged }: { event: AdminEvent; onChanged: () => void }) {
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const queryClient = useQueryClient();
  const streamId = event.stream?.id;

  const create = useMutation({ mutationFn: () => adminApi.createStream(event.id), onSuccess: onChanged });
  const details = useQuery({ queryKey: ['admin-stream', streamId], queryFn: () => adminApi.stream(streamId!), enabled: !!streamId });
  const status = useQuery({
    queryKey: ['admin-stream-status', streamId],
    queryFn: () => adminApi.streamStatus(streamId!),
    enabled: !!streamId,
    refetchInterval: statusPollMs,
  });

  // A status change (signal started/stopped) also changes the event's status: reload it right away.
  const providerStatus = status.data?.status;
  useEffect(() => {
    if (providerStatus) onChanged();
  }, [providerStatus, onChanged]);

  if (!streamId) {
    return (
      <div className="panel">
        <h2>{t('stream.title')}</h2>
        <p className="small muted">{t('stream.createHelp')}</p>
        <button type="button" className="button button-primary" disabled={create.isPending} onClick={() => create.mutate()}>
          {t('stream.create')}
        </button>
        {create.isError ? <ErrorMessage>{errorText(create.error)}</ErrorMessage> : null}
      </div>
    );
  }

  return (
    <div className="panel">
      <div className="row-between">
        <h2>{t('stream.title')}</h2>
        <StatusBadge status={status.data?.status} stored={status.data?.source === 'stored'} />
      </div>
      {details.isPending ? <Loading /> : null}
      {details.isError ? <ErrorMessage>{errorText(details.error)}</ErrorMessage> : null}
      {details.data ? (
        <>
          <CopyField label={t('stream.server')} value={details.data.rtmpUrl} />
          <CopyField label={t('stream.serverSecure')} value={details.data.rtmpsUrl} />
          <StreamKeyField streamId={streamId} streamKey={details.data.streamKey} />
          <p className="small muted">{t('stream.help')}</p>
          <Recordings
            streamId={streamId}
            recordings={details.data.recordings}
            onChanged={() => void queryClient.invalidateQueries({ queryKey: ['admin-stream', streamId] })}
          />
        </>
      ) : null}
      {import.meta.env.DEV ? <DevSimulation streamId={streamId} /> : null}
    </div>
  );
}

function StatusBadge({ status, stored }: { status: string | undefined; stored: boolean }) {
  const { t } = useTranslation('admin');
  const label =
    status === 'active' ? t('stream.statusActive') : status === 'idle' ? t('stream.statusIdle') : status === 'disabled' ? t('stream.statusDisabled') : t('stream.statusUnknown');
  return (
    <span className={`badge ${status === 'active' ? 'badge-live' : ''}`}>
      {label} {stored ? t('stream.statusStored') : ''}
    </span>
  );
}

function CopyField({ label, value, secret = false }: { label: string; value: string; secret?: boolean }) {
  const { t } = useTranslation('admin');
  const [revealed, setRevealed] = useState(!secret);
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    await navigator.clipboard.writeText(value);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1500);
  };

  return (
    <div className="copy-field">
      <span className="small muted">{label}</span>
      <div className="copy-row">
        <code>{revealed ? value : '•'.repeat(24)}</code>
        {secret ? (
          <button type="button" className="button" onClick={() => setRevealed(!revealed)}>
            {revealed ? t('stream.hide') : t('stream.show')}
          </button>
        ) : null}
        <button type="button" className="button" onClick={() => void copy()}>
          {copied ? t('stream.copied') : t('stream.copy')}
        </button>
      </div>
    </div>
  );
}

function StreamKeyField({ streamId, streamKey }: { streamId: string; streamKey: string }) {
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const queryClient = useQueryClient();
  const reset = useMutation({
    mutationFn: () => adminApi.resetStreamKey(streamId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['admin-stream', streamId] }),
  });

  return (
    <>
      <CopyField label={t('stream.streamKey')} value={streamKey} secret />
      <button
        type="button"
        className="link-button small"
        disabled={reset.isPending}
        onClick={() => {
          if (window.confirm(t('stream.resetConfirm'))) reset.mutate();
        }}
      >
        {t('stream.resetKey')}
      </button>
      {reset.isError ? <ErrorMessage>{errorText(reset.error)}</ErrorMessage> : null}
    </>
  );
}

function Recordings({ streamId, recordings, onChanged }: { streamId: string; recordings: { id: string; status: string; durationSeconds: number | null; isPrimary: boolean; createdAt: string }[]; onChanged: () => void }) {
  const { t } = useTranslation('admin');
  const select = useMutation({ mutationFn: (recordingId: string) => adminApi.selectPrimaryRecording(streamId, recordingId), onSuccess: onChanged });

  return (
    <div>
      <h3>{t('stream.recordings')}</h3>
      {recordings.length === 0 ? <p className="small muted">{t('stream.noRecordings')}</p> : null}
      <ul className="recording-list">
        {recordings.map((r) => (
          <li key={r.id}>
            <span>
              {formatShortDate(r.createdAt)} · {r.durationSeconds ? `${Math.round(r.durationSeconds / 60)} min` : r.status}
            </span>
            {r.isPrimary ? (
              <span className="badge">{t('stream.primary')}</span>
            ) : r.status === 'ready' ? (
              <button type="button" className="button" onClick={() => select.mutate(r.id)}>
                {t('stream.usePrimary')}
              </button>
            ) : null}
          </li>
        ))}
      </ul>
    </div>
  );
}

function DevSimulation({ streamId }: { streamId: string }) {
  const { t } = useTranslation('admin');
  const queryClient = useQueryClient();
  const simulate = useMutation({
    mutationFn: (status: 'active' | 'idle') => adminApi.simulateStream(streamId, status),
    onSuccess: () => void queryClient.invalidateQueries(),
  });

  return (
    <div className="dev-box">
      <span className="small muted">{t('stream.simulate')}</span>
      <div className="copy-row">
        <button type="button" className="button" onClick={() => simulate.mutate('active')}>
          {t('stream.simulateStart')}
        </button>
        <button type="button" className="button" onClick={() => simulate.mutate('idle')}>
          {t('stream.simulateStop')}
        </button>
      </div>
    </div>
  );
}
