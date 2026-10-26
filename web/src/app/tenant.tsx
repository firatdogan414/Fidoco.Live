import { createContext, useContext, useEffect, type ReactNode } from 'react';
import type { TenantBranding } from '../api/types';

const TenantContext = createContext<TenantBranding | null>(null);

/** Applies the tenant's colours as CSS variables and exposes its branding to the UI. */
export function TenantProvider({ tenant, children }: { tenant: TenantBranding; children: ReactNode }) {
  useEffect(() => {
    const root = document.documentElement;
    root.style.setProperty('--color-primary', tenant.primaryColor);
    root.style.setProperty('--color-accent', tenant.accentColor);
    document.title = tenant.name;
  }, [tenant]);

  return <TenantContext.Provider value={tenant}>{children}</TenantContext.Provider>;
}

export function useTenant(): TenantBranding {
  const tenant = useContext(TenantContext);
  if (!tenant) {
    throw new Error('useTenant must be used inside TenantProvider');
  }
  return tenant;
}
