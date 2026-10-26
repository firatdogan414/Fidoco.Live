import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { adminApi } from '../../api/admin';
import { ErrorMessage, Notice } from '../../shared/components';
import { useErrorText } from '../../shared/errors';
import { formatShortDate } from '../../shared/format';

export function AdminUsersPage() {
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const queryClient = useQueryClient();
  const users = useQuery({ queryKey: ['admin-users'], queryFn: adminApi.users });
  const [email, setEmail] = useState('');
  const invite = useMutation({
    mutationFn: () => adminApi.inviteUser(email),
    onSuccess: () => {
      setEmail('');
      void queryClient.invalidateQueries({ queryKey: ['admin-users'] });
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    invite.mutate();
  };

  return (
    <section className="narrow">
      <h1>{t('users.title')}</h1>
      <ul className="purchase-list">
        {users.data?.map((u) => (
          <li key={u.id}>
            <span>{u.email}</span>
            <span className="small muted">
              {u.hasPassword ? (u.lastLoginAt ? `${t('users.lastLogin')}: ${formatShortDate(u.lastLoginAt)}` : '') : t('users.pending')}
            </span>
          </li>
        ))}
      </ul>
      <form className="form panel" onSubmit={submit}>
        <p className="small muted">{t('users.inviteHelp')}</p>
        <label>
          {t('login.email')}
          <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <button type="submit" className="button button-primary" disabled={invite.isPending}>
          {t('users.invite')}
        </button>
        {invite.isSuccess ? <Notice>{t('users.invited')}</Notice> : null}
        {invite.isError ? <ErrorMessage>{errorText(invite.error)}</ErrorMessage> : null}
      </form>
    </section>
  );
}
