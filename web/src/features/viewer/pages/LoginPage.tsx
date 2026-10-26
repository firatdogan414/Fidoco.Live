import { useMutation } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import { viewerApi } from '../../../api/viewer';
import { ErrorMessage, Notice } from '../../../shared/components';
import { useErrorText } from '../../../shared/errors';

export function LoginPage() {
  const { t } = useTranslation('viewer');
  const errorText = useErrorText();
  const [params] = useSearchParams();
  const [email, setEmail] = useState('');
  const send = useMutation({ mutationFn: () => viewerApi.requestLoginLink(email, params.get('returnPath') ?? undefined) });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    send.mutate();
  };

  return (
    <section className="narrow">
      <h1>{t('login.title')}</h1>
      {send.isSuccess ? (
        <Notice>{t('login.sent')}</Notice>
      ) : (
        <form onSubmit={submit} className="form">
          <p>{t('login.intro')}</p>
          <label>
            {t('login.email')}
            <input type="email" required autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} />
          </label>
          <button type="submit" className="button button-primary" disabled={send.isPending}>
            {t('login.submit')}
          </button>
          {send.isError ? <ErrorMessage>{errorText(send.error)}</ErrorMessage> : null}
        </form>
      )}
    </section>
  );
}
