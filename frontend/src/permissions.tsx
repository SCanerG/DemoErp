import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from './Auth';
import type { User } from './types';
import { t } from './i18n';

export type Permission = 'read' | 'write' | 'delete' | 'users' | 'audit';
export function allowed(user: Pick<User, 'role'> | undefined, permission: Permission) {
  const role = user?.role;
  if (!role) return false;
  if (permission === 'read') return true;
  if (permission === 'write') return role === 'Admin' || role === 'Manager';
  return role === 'Admin';
}
export function Can({ permission, children }: { permission: Permission; children: ReactNode }) {
  const { session } = useAuth();
  return allowed(session?.user, permission) ? children : null;
}
export function AccessDenied() {
  return <section className="panel"><h1 className="text-3xl font-semibold">{t('Access Denied')}</h1><p className="my-5 text-slate-500">{t('You do not have permission to access this page.')}</p><Link className="button" to="/products">{t('Go to products')}</Link></section>;
}
export function RequirePermission({ permission, children }: { permission: Permission; children: ReactNode }) {
  const { session } = useAuth();
  return allowed(session?.user, permission) ? children : <AccessDenied />;
}
