import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { adminApi, type AdminEvent } from '../../api/admin';
import { ErrorMessage, Notice } from '../../shared/components';
import { useErrorText } from '../../shared/errors';
import { formatShortDate } from '../../shared/format';

/** Complimentary tickets (friplatser): free access for an e-mail address, kept apart from sales. */
export function CompsPanel({ event }: { event: AdminEvent }) {
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const queryClient = useQueryClient();
  const comps = useQuery({ queryKey: ['admin-comps', event.id], queryFn: () => adminApi.comps(event.id) });
  const [email, setEmail] = useState('');
  const [note, setNote] = useState('');

  const changed = () => {
    void queryClient.invalidateQueries({ queryKey: ['admin-comps', event.id] });
    void queryClient.invalidateQueries({ queryKey: ['admin-settlement', event.id] });
    void queryClient.invalidateQueries({ queryKey: ['support-log'] });
  };
  const grant = useMutation({
    mutationFn: () => adminApi.grantComp(event.id, { email, ticketTypeId: event.ticket!.id, note: note.trim() || null }),
    onSuccess: () => {
      setEmail('');
      setNote('');
      changed();
    },
  });
  const revoke = useMutation({ mutationFn: adminApi.revokeComp, onSuccess: changed });

  if (!event.ticket) return null;
  const list = comps.data ?? [];
  const active = list.filter((c) => !c.revoked).length;

  const submit = (e: FormEvent) => {
    e.preventDefault();
    grant.mutate();
  };

  return (
    <div className="panel">
      <h2>{t('comps.title', { count: active })}</h2>
      <p className="small muted">{t('comps.help')}</p>
      {comps.isError ? <ErrorMessage>{errorText(comps.error)}</ErrorMessage> : null}
      {list.length > 0 ? (
        <ul className="purchase-list">
          {list.map((c) => (
            <li key={c.entitlementId}>
              <div>
                <strong>{c.email ?? t('support.erasedCustomer')}</strong>
                {c.note ? ` · ${c.note}` : ''}
                <div className="small muted">
                  {t('comps.grantedBy', { admin: c.grantedBy ?? '–', date: formatShortDate(c.grantedAt) })}
                  {c.revokedAt ? ` · ${t('comps.revokedAt', { date: formatShortDate(c.revokedAt) })}` : ''}
                </div>
              </div>
              {c.revoked ? (
                <span className="badge">{t('comps.revoked')}</span>
              ) : (
                <button
                  type="button"
                  className="button button-danger"
                  disabled={revoke.isPending}
                  onClick={() => {
                    if (window.confirm(t('comps.revokeConfirm', { email: c.email ?? '' }))) revoke.mutate(c.entitlementId);
                  }}
                >
                  {t('comps.revoke')}
                </button>
              )}
            </li>
          ))}
        </ul>
      ) : null}
      {revoke.isError ? <ErrorMessage>{errorText(revoke.error)}</ErrorMessage> : null}

      <form className="form" onSubmit={submit}>
        <label>
          {t('comps.email')}
          <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label>
          {t('comps.note')}
          <input type="text" maxLength={200} value={note} onChange={(e) => setNote(e.target.value)} />
        </label>
        <button type="submit" className="button button-primary" disabled={grant.isPending}>
          {t('comps.grant')}
        </button>
        {grant.isSuccess ? <Notice>{t('comps.granted')}</Notice> : null}
        {grant.isError ? <ErrorMessage>{errorText(grant.error)}</ErrorMessage> : null}
      </form>
    </div>
  );
}
