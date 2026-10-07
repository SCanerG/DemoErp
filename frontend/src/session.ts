import type { Session } from './types';
import { z } from 'zod';

const sessionSchema = z.object({
  accessToken: z.string().min(1), expiresAt: z.string().refine(value => Date.parse(value) > Date.now()),
  user: z.object({ id: z.string().min(1), name: z.string().min(1), email: z.string().email() }),
});

const key = 'catalog.session';
let current: Session | null = read();
const listeners = new Set<() => void>();

function read(): Session | null {
  try {
    const parsed = sessionSchema.safeParse(JSON.parse(sessionStorage.getItem(key) ?? 'null'));
    if (parsed.success) return parsed.data;
    sessionStorage.removeItem(key);
  } catch { /* Invalid or unavailable storage starts an unauthenticated session. */ }
  return null;
}

export const sessionStore = {
  get: () => current,
  subscribe: (callback: () => void) => { listeners.add(callback); return () => { listeners.delete(callback); }; },
  set: (session: Session | null) => {
    current = session;
    try {
      if (session) sessionStorage.setItem(key, JSON.stringify(session));
      else sessionStorage.removeItem(key);
    } catch { /* Keep the in-memory session usable when browser storage is unavailable. */ }
    listeners.forEach(callback => callback());
  },
};
