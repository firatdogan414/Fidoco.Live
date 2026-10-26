import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { lazy, Suspense } from 'react';
import { useTranslation } from 'react-i18next';
import { BrowserRouter, Route, Routes } from 'react-router';
import { viewerApi } from '../api/viewer';
import { AccountPage } from '../features/viewer/pages/AccountPage';
import { EventListPage } from '../features/viewer/pages/EventListPage';
import { EventPage } from '../features/viewer/pages/EventPage';
import { LegalPage } from '../features/viewer/pages/LegalPage';
import { LoginPage } from '../features/viewer/pages/LoginPage';
import { ThankYouPage } from '../features/viewer/pages/ThankYouPage';
import { VerifyPage } from '../features/viewer/pages/VerifyPage';
import { ViewerLayout } from '../features/viewer/ViewerLayout';
import { ErrorMessage, Loading } from '../shared/components';
import { isApiError, useErrorText } from '../shared/errors';
import { TenantProvider } from './tenant';

// The watch page carries the video player (large); load it only when needed.
const WatchPage = lazy(() => import('../features/viewer/pages/WatchPage').then((m) => ({ default: m.WatchPage })));

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: 1, refetchOnWindowFocus: false },
  },
});

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <SiteRoutes />
      </BrowserRouter>
    </QueryClientProvider>
  );
}

/** The host decides the site: a tenant (viewer + /admin) or the platform host (platform admin). */
function SiteRoutes() {
  const { t } = useTranslation();
  const errorText = useErrorText();
  const site = useQuery({ queryKey: ['tenant'], queryFn: viewerApi.tenant, staleTime: Infinity, retry: false });

  if (site.isPending) return <Loading />;
  if (site.isError) {
    return (
      <main className="page">
        <ErrorMessage>{isApiError(site.error, 404) ? t('app.unknownSite') : errorText(site.error)}</ErrorMessage>
      </main>
    );
  }

  if (site.data.kind === 'platform' || !site.data.tenant) {
    return (
      <main className="page">
        <h1>Fidoco</h1>
      </main>
    );
  }

  return (
    <TenantProvider tenant={site.data.tenant}>
      <Routes>
        <Route element={<ViewerLayout />}>
          <Route index element={<EventListPage />} />
          <Route path="e/:slug" element={<EventPage />} />
          <Route path="e/:slug/tack" element={<ThankYouPage />} />
          <Route
            path="e/:slug/titta"
            element={
              <Suspense fallback={<Loading />}>
                <WatchPage />
              </Suspense>
            }
          />
          <Route path="logga-in" element={<LoginPage />} />
          <Route path="auth/verifiera" element={<VerifyPage />} />
          <Route path="konto" element={<AccountPage />} />
          <Route path="kopvillkor" element={<LegalPage kind="terms" />} />
          <Route path="integritet" element={<LegalPage kind="privacy" />} />
          <Route path="*" element={<p>{t('app.notFound')}</p>} />
        </Route>
      </Routes>
    </TenantProvider>
  );
}
