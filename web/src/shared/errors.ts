import { useTranslation } from 'react-i18next';
import { ApiError } from '../api/client';

/** Translates an API error code (errors.<code>) into a user-facing Swedish text. */
export function useErrorText() {
  const { t } = useTranslation();
  return (error: unknown): string => {
    const code = error instanceof ApiError ? error.code : 'request_failed';
    return t(`errors.${code}`, { defaultValue: t('errors.request_failed') });
  };
}

export function isApiError(error: unknown, status?: number): error is ApiError {
  return error instanceof ApiError && (status === undefined || error.status === status);
}
