import { expect, type APIRequestContext } from '@playwright/test';
import type { Session, User, UserRole } from '../src/types';

const base = `${process.env.API_URL ?? 'http://localhost:5080'}/api`;
let cached: Session | undefined;
export async function adminSession(request: APIRequestContext): Promise<Session> {
  if (cached) return cached;
  const email = process.env.BOOTSTRAP_ADMIN_EMAIL;
  const password = process.env.BOOTSTRAP_ADMIN_PASSWORD;
  if (!email || !password) throw new Error('Use a disposable bootstrapped stack and provide ignored environment credentials. See docs/DEVELOPMENT.md.');
  const response = await request.post(`${base}/auth/login`, { data: { email, password } }); expect(response.status()).toBe(200);
  cached = await response.json(); return cached!;
}
export async function promoteRegistered(request: APIRequestContext, email: string, role: UserRole = 'Admin') {
  const admin = await adminSession(request); const headers = { Authorization: `Bearer ${admin.accessToken}` };
  const response = await request.get(`${base}/users`, { headers }); expect(response.status()).toBe(200);
  const user = ((await response.json()) as User[]).find(value => value.email === email.toLowerCase()); expect(user).toBeDefined();
  expect((await request.put(`${base}/users/${user!.id}/role`, { headers, data: { role } })).status()).toBe(200);
}
export async function createAccount(request: APIRequestContext, role: UserRole) {
  const admin = await adminSession(request); const email = `role-${role}-${Date.now()}@example.com`; const password = 'BrowserRoleTest123!';
  const response = await request.post(`${base}/users`, { headers: { Authorization: `Bearer ${admin.accessToken}` }, data: { name: `Browser ${role}`, email, password, role } }); expect(response.status()).toBe(201);
  const login = await request.post(`${base}/auth/login`, { data: { email, password } }); expect(login.status()).toBe(200);
  return { session: (await login.json()) as Session, email, password };
}
