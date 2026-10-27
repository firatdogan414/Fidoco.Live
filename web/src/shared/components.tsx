import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

export function Loading() {
  const { t } = useTranslation();
  return <p className="muted center">{t('app.loading')}</p>;
}

export function ErrorMessage({ children }: { children: ReactNode }) {
  return (
    <p className="alert" role="alert">
      {children}
    </p>
  );
}

export function Notice({ children }: { children: ReactNode }) {
  return <p className="notice">{children}</p>;
}
