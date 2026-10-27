import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link, Navigate, Outlet, Route, Routes, useLocation } from 'react-router';
import { adminApi } from '../../api/admin';
import { useTenant } from '../../app/tenant';
import { Loading } from '../../shared/components';
import { AdminEventPage } from './AdminEventPage';
import { AdminEventsPage } from './AdminEventsPage';
import { AdminLoginPage } from './AdminLoginPage';
import { AdminNewEventPage } from './AdminNewEventPage';
import { AdminProfilePage } from './AdminProfilePage';
import { AdminSupportPage } from './AdminSupportPage';
import { AdminUsersPage } from './AdminUsersPage';
import { ForgotPasswordPage, SetPasswordPage } from '../shared-auth/PasswordPages';
import { useAdminMe } from './useAdminMe';

/** The organizer's admin (TenantAdmin) under /admin on the tenant's own host. */
export function AdminApp() {
  return (
    <Routes>
      <Route path="logga-in" element={<AdminLoginPage />} />
      <Route path="valj-losenord" element={<SetPasswordPage area="admin" loginPath="/admin/logga-in" />} />
      <Route path="glomt-losenord" element={<ForgotPasswordPage area="admin" />} />
      <Route element={<RequireAdmin />}>
        <Route index element={<AdminEventsPage />} />
        <Route path="event/ny" element={<AdminNewEventPage />} />
        <Route path="event/:id" element={<AdminEventPage />} />
        <Route path="anvandare" element={<AdminUsersPage />} />
        <Route path="support" element={<AdminSupportPage />} />
        <Route path="profil" element={<AdminProfilePage />} />
      </Route>
    </Routes>
  );
}

function RequireAdmin() {
  const me = useAdminMe();
  const location = useLocation();
  if (me.isPending) return <Loading />;
  if (!me.data) return <Navigate to="/admin/logga-in" replace state={{ from: location.pathname }} />;
  return <AdminLayout />;
}

function AdminLayout() {
  const { t } = useTranslation('admin');
  const tenant = useTenant();
  const queryClient = useQueryClient();

  const logout = async () => {
    await adminApi.logout();
    await queryClient.invalidateQueries({ queryKey: ['admin-me'] });
  };

  return (
    <div className="layout">
      <header className="site-header">
        <Link to="/admin" className="brand">
          <span>
            {tenant.name} · {t('title')}
          </span>
        </Link>
        <nav className="site-nav">
          <Link to="/admin/support">{t('nav.support')}</Link>
          <Link to="/admin/profil">{t('nav.profile')}</Link>
          <Link to="/admin/anvandare">{t('nav.users')}</Link>
          <Link to="/">{t('nav.viewSite')}</Link>
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
