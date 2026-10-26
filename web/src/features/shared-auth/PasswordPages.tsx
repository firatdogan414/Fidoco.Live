import { useMutation } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { passwordApi, type AdminArea } from '../../api/platform';
import { ErrorMessage, Notice } from '../../shared/components';
import { useErrorText } from '../../shared/errors';

/** Choose a password from an invitation or reset link (tenant admin or platform owner). */
export function SetPasswordPage({ area, loginPath }: { area: AdminArea; loginPath: string }) {
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const [params] = useSearchParams();
  const [password, setPassword] = useState('');
  const save = useMutation({ mutationFn: () => passwordApi.setPassword(area, params.get('token') ?? '', password) });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  return (
    <main className="page">
      <section className="narrow">
        <h1>{t('password.setTitle')}</h1>
        {save.isSuccess ? (
          <>
            <Notice>{t('password.done')}</Notice>
            <Link className="button button-primary" to={loginPath}>
              {t('password.toLogin')}
            </Link>
          </>
        ) : (
          <form className="form" onSubmit={submit}>
            <label>
              {t('password.newPassword')}
              <input type="password" required minLength={10} autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} />
            </label>
            <button type="submit" className="button button-primary" disabled={save.isPending}>
              {t('password.save')}
            </button>
            {save.isError ? <ErrorMessage>{errorText(save.error)}</ErrorMessage> : null}
          </form>
        )}
      </section>
    </main>
  );
}

export function ForgotPasswordPage({ area }: { area: AdminArea }) {
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const [email, setEmail] = useState('');
  const send = useMutation({ mutationFn: () => passwordApi.requestReset(area, email) });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    send.mutate();
  };

  return (
    <main className="page">
      <section className="narrow">
        <h1>{t('password.forgotTitle')}</h1>
        {send.isSuccess ? (
          <Notice>{t('password.sent')}</Notice>
        ) : (
          <form className="form" onSubmit={submit}>
            <p>{t('password.forgotIntro')}</p>
            <label>
              {t('login.email')}
              <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
            </label>
            <button type="submit" className="button button-primary" disabled={send.isPending}>
              {t('password.send')}
            </button>
            {send.isError ? <ErrorMessage>{errorText(send.error)}</ErrorMessage> : null}
          </form>
        )}
      </section>
    </main>
  );
}
