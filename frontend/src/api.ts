import { sessionStore } from './session';
import { z } from 'zod';
import { t, getLanguage } from './i18n';

const problemSchema = z.object({
  title: z.string().optional(), detail: z.string().optional(), code: z.string().optional(),
  errors: z.record(z.string(), z.array(z.string())).optional(),
});

interface ApiProblem { title?: string; detail?: string; code?: string; errors?: Record<string, string[]> }

export class ApiError extends Error {
  constructor(public status: number, public problem: ApiProblem) {
    super(problem.detail ?? problem.title ?? `Request failed (${status}).`);
  }
}

const baseUrl = import.meta.env.VITE_API_BASE_URL;
if (!baseUrl) throw new Error('VITE_API_BASE_URL is required. See .env.example.');

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const session = sessionStore.get();
  const headers = new Headers(options.headers);
  if (options.body) headers.set('Content-Type', 'application/json');
  const protectedRequest = !path.startsWith('/auth/');
  if (protectedRequest && session) headers.set('Authorization', `Bearer ${session.accessToken}`);
  let response: Response;
  try { response = await fetch(`${baseUrl.replace(/\/$/, '')}${path}`, { ...options, headers }); }
  catch (error) {
    if (options.signal?.aborted) throw error;
    throw new Error('Unable to reach the server. Check your connection and try again.');
  }
  if (!response.ok) {
    const parsed = problemSchema.safeParse(await response.json().catch(() => null));
    const problem: ApiProblem = parsed.success ? parsed.data : { title: 'Unable to complete the request.' };
    if (response.status === 401 && protectedRequest
      && session?.accessToken === sessionStore.get()?.accessToken) sessionStore.set(null);
    throw new ApiError(response.status, problem);
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

export function errorText(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.problem.code) return t(error.problem.code);
    if (error.status === 400) return t('Check the submitted fields.');
    if (error.status === 401) return t(error.problem.detail === 'Email or password is incorrect.' ? 'Email or password is incorrect.' : 'Please sign in again.');
    if (error.status === 409) return t('This email is already registered.');
    if (error.status === 404) return t('Record not found');
    if (error.status === 429) return t('Wait a minute and try again.');
    return getLanguage() === 'tr' ? t('Please try again later.') : t(error.message);
  }
  return t(error instanceof Error ? error.message : 'Something went wrong. Please try again.');
}
