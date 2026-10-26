import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, Navigate, Outlet, Route, Routes } from 'react-router';
import { platformApi } from '../../api/platform';
import { ErrorMessage, Loading } from '../../shared/components';
import { isApiError, useErrorText } from '../../shared/errors';
import { ForgotPasswordPage, SetPasswordPage } from '../shared-auth/PasswordPages';
import { SettlementsPage } from './SettlementsPage';
import { TenantPage } from './TenantPage';
import { TenantsPage } from './TenantsPage';

/** The platform owner's admin on the platform host (e.g. admin.fidoco.se). */
export function PlatformApp() {
  return (
    <Routes>
      <Route path="logga-in" element={<PlatformLoginPage />} />
      <Route path="valj-losenord" element={<SetPasswordPage area="platform" loginPath="/logga-in" />} />
      <Route path="glomt-losenord" element={<ForgotPasswordPage area="platform" />} />
      <Route element={<RequirePlatformAdmin />}>
        <Route index element={<TenantsPage />} />
        <Route path="arrangorer/:id" element={<TenantPage />} />
        <Route path="avrakning" element={<SettlementsPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

function usePlatformMe() {
  return useQuery({
    queryKey: ['platform-me'],
    queryFn: async () => {
      try {
        return await platformApi.me();
      } catch (error) {
        if (isApiError(error, 401) || isApiError(error, 403)) return null;
        throw error;
      }
    },
  });
}

function RequirePlatformAdmin() {
  const { t } = useTranslation('platform');
  const me = usePlatformMe();
  const queryClient = useQueryClient();
  if (me.isPending) return <Loading />;
  if (!me.data) return <Navigate to="/logga-in" replace />;

  const logout = async () => {
    await platformApi.logout();
    queryClient.setQueryData(['platform-me'], null);
  };

  return (
    <div className="layout">
      <header className="site-header">
        <Link to="/" className="brand">
          {t('title')}
        </Link>
        <nav className="site-nav">
          <Link to="/">{t('nav.tenants')}</Link>
          <Link to="/avrakning">{t('nav.settlements')}</Link>
          <button type="button" className="link-button" onClick={() => void logout()}>
            {t('nav.logout')}
          </button>
        </nav>
      </header>
      <main className="page">
        <Outlet />
      </main>
    </div>
  );
}

function PlatformLoginPage() {
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const queryClient = useQueryClient();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const login = useMutation({
    mutationFn: () => platformApi.login(email, password),
    onSuccess: (me) => queryClient.setQueryData(['platform-me'], me),
  });

  if (login.isSuccess) return <Navigate to="/" replace />;

  const submit = (e: FormEvent) => {
    e.preventDefault();
    login.mutate();
  };

  return (
    <main className="page">
      <section className="narrow">
        <h1>Fidoco</h1>
        <form className="form" onSubmit={submit}>
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
          <Link to="/glomt-losenord" className="small">
            {t('login.forgot')}
          </Link>
        </form>
      </section>
    </main>
  );
}
