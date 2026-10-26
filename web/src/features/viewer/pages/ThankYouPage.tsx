import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { viewerApi } from '../../../api/viewer';
import { ErrorMessage, Loading, Notice } from '../../../shared/components';
import { useErrorText } from '../../../shared/errors';
import { useNow } from '../../../shared/hooks';

/** Max time to wait for the payment webhook before telling the buyer to watch their inbox. */
const slowAfterMs = 120_000;

/**
 * Back from Stripe: waits for the payment to be confirmed. The browser that started the checkout is
 * signed in by the server and sent straight to the watch page once access is granted.
 */
export function ThankYouPage() {
  const { slug = '' } = useParams();
  const [params] = useSearchParams();
  const checkoutId = params.get('c') ?? '';
  const { t } = useTranslation('viewer');
  const errorText = useErrorText();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [startedAt] = useState(() => Date.now());
  const now = useNow(5000);

  const checkout = useQuery({
    queryKey: ['checkout', checkoutId],
    queryFn: () => viewerApi.checkout(checkoutId),
    enabled: !!checkoutId,
    refetchInterval: (query) => (query.state.data?.status === 'pending' && Date.now() - startedAt < slowAfterMs ? 2000 : false),
  });

  const completed = checkout.data?.status === 'completed';
  const signedIn = checkout.data?.signedIn ?? false;
  const access = useQuery({
    queryKey: ['access', checkout.data?.eventId],
    queryFn: () => viewerApi.access(checkout.data!.eventId),
    enabled: completed && signedIn,
    refetchInterval: (query) => (query.state.data?.hasTicket ? false : 1000),
  });

  useEffect(() => {
    if (signedIn) void queryClient.invalidateQueries({ queryKey: ['me'] });
  }, [signedIn, queryClient]);

  useEffect(() => {
    if (access.data?.hasTicket) void navigate(`/e/${slug}/titta`, { replace: true });
  }, [access.data?.hasTicket, navigate, slug]);

  if (!checkoutId) return <ErrorMessage>{t('thanks.expired')}</ErrorMessage>;
  if (checkout.isError) return <ErrorMessage>{errorText(checkout.error)}</ErrorMessage>;
  if (checkout.isPending) return <Loading />;

  const status = checkout.data.status;
  return (
    <section className="center">
      <h1>{t('thanks.title')}</h1>
      {status === 'pending' ? (
        <Notice>{now - startedAt < slowAfterMs ? t('thanks.waiting') : t('thanks.slow')}</Notice>
      ) : null}
      {status === 'completed' && signedIn ? <Notice>{t('thanks.ready')}</Notice> : null}
      {status === 'completed' && !signedIn ? (
        <>
          <Notice>{t('thanks.checkEmail')}</Notice>
          <Link className="button button-primary" to={`/e/${slug}/titta`}>
            {t('thanks.goToWatch')}
          </Link>
        </>
      ) : null}
      {status === 'expired' || status === 'failed' ? <ErrorMessage>{t('thanks.expired')}</ErrorMessage> : null}
    </section>
  );
}
