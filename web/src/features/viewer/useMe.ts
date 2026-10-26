import { useQuery } from '@tanstack/react-query';
import { viewerApi } from '../../api/viewer';
import { isApiError } from '../../shared/errors';

/** The signed-in customer, or null. */
export function useMe() {
  return useQuery({
    queryKey: ['me'],
    queryFn: async () => {
      try {
        return await viewerApi.me();
      } catch (error) {
        if (isApiError(error, 401) || isApiError(error, 403)) return null;
        throw error;
      }
    },
    staleTime: 60_000,
  });
}
