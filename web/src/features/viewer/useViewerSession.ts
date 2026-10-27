import { useQuery } from '@tanstack/react-query';
import { viewerApi } from '../../api/viewer';
import { isApiError } from '../../shared/errors';

/**
 * Whether this browser may watch: a full account (magic link) or a session limited to the purchase it
 * just paid for. Account pages use useMe instead, which is null for limited sessions.
 */
export function useViewerSession() {
  return useQuery({
    queryKey: ['viewer-session'],
    queryFn: async () => {
      try {
        return await viewerApi.session();
      } catch (error) {
        if (isApiError(error, 401) || isApiError(error, 403)) return null;
        throw error;
      }
    },
    staleTime: 60_000,
  });
}
