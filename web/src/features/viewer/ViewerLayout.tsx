import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link, Outlet } from 'react-router';
import { viewerApi } from '../../api/viewer';
import { useTenant } from '../../app/tenant';
import { useMe } from './useMe';
import { useViewerSession } from './useViewerSession';

export function ViewerLayout() {
  const { t } = useTranslation();
  const tenant = useTenant();
  const me = useMe();
  const session = useViewerSession();
  const queryClient = useQueryClient();

  const logout = async () => {
    await viewerApi.logout();
    await queryClient.invalidateQueries();
  };

  return (
    <div className="layout">
      <header className="site-header">
        <Link to="/" className="brand">
          {tenant.logoUrl ? <img src={tenant.logoUrl} alt="" className="brand-logo" /> : null}
          <span>{tenant.name}</span>
        </Link>
        <nav className="site-nav">
          {me.data ? (
            <>
              <Link to="/konto">{t('nav.account')}</Link>
              <button type="button" className="link-button" onClick={() => void logout()}>
                {t('nav.logout')}
              </button>
            </>
          ) : (
            <>
              {/* A purchase-only session (right after paying) signs in with the magic link for account access. */}
              <Link to="/logga-in">{t('nav.login')}</Link>
              {session.data ? (
                <button type="button" className="link-button" onClick={() => void logout()}>
                  {t('nav.logout')}
                </button>
              ) : null}
            </>
          )}
        </nav>
      </header>
      <main className="page">
        <Outlet />
      </main>
      <footer className="site-footer">
        <Link to="/kopvillkor">{t('app.terms')}</Link>
        <Link to="/integritet">{t('app.privacy')}</Link>
        {tenant.supportEmail ? <a href={`mailto:${tenant.supportEmail}`}>{tenant.supportEmail}</a> : null}
        <span className="muted">{t('app.poweredBy')}</span>
      </footer>
    </div>
  );
}
