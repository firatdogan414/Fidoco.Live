import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { viewerApi } from '../../../api/viewer';
import { ErrorMessage, Loading } from '../../../shared/components';
import { useErrorText } from '../../../shared/errors';

/**
 * Landing page of a magic link. The token is sent with a POST from here (never consumed by a GET),
 * so e-mail link scanners cannot use up single-use links.
 */
export function VerifyPage() {
  const { t } = useTranslation('viewer');
  const errorText = useErrorText();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [error, setError] = useState<unknown>(null);
  const sent = useRef(false);

  useEffect(() => {
    if (sent.current) return; // StrictMode runs effects twice in development; send once.
    sent.current = true;
    viewerApi
      .verify(params.get('token') ?? '')
      .then(async (result) => {
        await queryClient.invalidateQueries();
        void navigate(result.returnPath || '/', { replace: true });
      })
      .catch(setError);
  }, [params, navigate, queryClient]);

  if (error) {
    return (
      <section className="narrow">
        <ErrorMessage>{errorText(error)}</ErrorMessage>
        <Link to="/logga-in">{t('login.title')}</Link>
      </section>
    );
  }

  return (
    <section className="narrow">
      <p>{t('login.verifying')}</p>
      <Loading />
    </section>
  );
}
