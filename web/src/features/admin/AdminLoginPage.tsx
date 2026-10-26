import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate } from 'react-router';
import { adminApi } from '../../api/admin';
import { ErrorMessage } from '../../shared/components';
import { useErrorText } from '../../shared/errors';

export function AdminLoginPage() {
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');

  const login = useMutation({
    mutationFn: () => adminApi.login(email, password),
    onSuccess: (me) => {
      // Set the signed-in admin directly: a cached "not signed in" would otherwise bounce back here.
      queryClient.setQueryData(['admin-me'], me);
      const from = (location.state as { from?: string } | null)?.from;
      void navigate(from ?? '/admin', { replace: true });
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    login.mutate();
  };

  return (
    <main className="page">
      <section className="narrow">
        <h1>{t('login.title')}</h1>
        <form onSubmit={submit} className="form">
          <label>
            {t('login.email')}
            <input type="email" required autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} />
          </label>
          <label>
            {t('login.password')}
            <input type="password" required autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} />
          </label>
          <button type="submit" className="button button-primary" disabled={login.isPending}>
            {t('login.submit')}
          </button>
          {login.isError ? <ErrorMessage>{errorText(login.error)}</ErrorMessage> : null}
        </form>
      </section>
    </main>
  );
}
