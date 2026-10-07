import { createContext, useContext, useEffect, useSyncExternalStore, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { sessionStore } from './session';
import type { Session } from './types';

const AuthContext = createContext<{ session: Session | null; login: (value: Session) => void; logout: () => void } | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const session = useSyncExternalStore(sessionStore.subscribe, sessionStore.get);
  const queryClient = useQueryClient();
  useEffect(() => { if (!session) queryClient.clear(); }, [session, queryClient]);
  useEffect(() => {
    if (!session) return;
    const delay = Date.parse(session.expiresAt) - Date.now();
    if (delay <= 0) { sessionStore.set(null); return; }
    const timer = window.setTimeout(() => sessionStore.set(null), delay);
    return () => window.clearTimeout(timer);
  }, [session]);
  return <AuthContext.Provider value={{ session,
    login: value => { queryClient.clear(); sessionStore.set(value); },
    logout: () => { sessionStore.set(null); queryClient.clear(); } }}>
    {children}
  </AuthContext.Provider>;
}

export function useAuth() {
  const auth = useContext(AuthContext);
  if (!auth) throw new Error('AuthProvider is required.');
  return auth;
}
