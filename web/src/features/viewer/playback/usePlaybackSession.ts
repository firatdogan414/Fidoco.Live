import { useEffect, useRef, useState } from 'react';
import { playbackApi } from '../../../api/viewer';
import { PlaybackSessionController, type PlaybackState } from './PlaybackSessionController';

/**
 * React wrapper around PlaybackSessionController: starts a session when <paramref name="enabled"/>
 * becomes true and ends it when the component unmounts or the page is closed.
 */
export function usePlaybackSession(eventId: string | undefined, enabled: boolean) {
  const [state, setState] = useState<PlaybackState>({ kind: 'idle' });
  const controllerRef = useRef<PlaybackSessionController | null>(null);

  useEffect(() => {
    if (!eventId || !enabled) return;
    const controller = new PlaybackSessionController(playbackApi, eventId, setState);
    controllerRef.current = controller;
    void controller.start();

    const onPageHide = () => controller.stop();
    window.addEventListener('pagehide', onPageHide);
    return () => {
      window.removeEventListener('pagehide', onPageHide);
      controller.stop();
      controllerRef.current = null;
    };
  }, [eventId, enabled]);

  return {
    state,
    takeOver: () => void controllerRef.current?.start(),
    refreshTokens: () => void controllerRef.current?.refreshTokens(),
  };
}
