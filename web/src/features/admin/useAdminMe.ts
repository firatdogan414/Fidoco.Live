import { useQuery } from '@tanstack/react-query';
import { adminApi } from '../../api/admin';
import { isApiError } from '../../shared/errors';

/** The signed-in admin, or null. */
export function useAdminMe() {
  return useQuery({
    queryKey: ['admin-me'],
    queryFn: async () => {
      try {
        return await adminApi.me();
      } catch (error) {
        if (isApiError(error, 401) || isApiError(error, 403)) return null;
        throw error;
      }
    },
    staleTime: 60_000,
  });
}
