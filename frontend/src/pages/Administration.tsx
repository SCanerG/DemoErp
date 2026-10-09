import { allowed } from '../permissions';
import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { api } from '../api';
import { useAuth } from '../Auth';
import { t } from '../i18n';
import { dateFormat, ErrorNotice, FieldError, Loading, QueryError, StatusBadge } from '../components';
import { DeleteConfirmation } from '../DeleteConfirmation';
import type { AuditLog, AuditPage, User, UserRole } from '../types';

const roles: UserRole[] = ['Viewer', 'Manager', 'Admin'];
type SecurityChange = { user: User; role?: UserRole; active?: boolean };

export function UsersPage() {
  const { session } = useAuth();
  const cache = useQueryClient();
  const [change, setChange] = useState<SecurityChange | null>(null);
  const query = useQuery({ queryKey: ['users'], queryFn: ({ signal }) => api<User[]>('/users', { signal }) });
  const save = useMutation({ mutationFn: (value: SecurityChange) => api<User>(`/users/${value.user.id}/${value.role ? 'role' : 'status'}`, { method: 'PUT', body: JSON.stringify(value.role ? { role: value.role } : { isActive: value.active }) }), onSuccess: async () => {
    await Promise.all([cache.invalidateQueries({ queryKey: ['users'] }), cache.invalidateQueries({ queryKey: ['audit-logs'] })]); setChange(null);
  } });
  if (query.isPending) return <Loading />;
  if (query.isError) return <QueryError error={query.error} retry={() => void query.refetch()} back="/users" />;
  function actions(user: User) {
    const self = user.id === session?.user.id;
    return <div className="flex flex-wrap items-center gap-3"><Link className="text-link" to={`/users/${user.id}/edit`}>{t('Edit')}</Link><label className="field text-xs">{t('Change role')}<select aria-label={`${t('Change role')} ${user.name}`} value={user.role} disabled={self || save.isPending} onChange={event => { save.reset(); setChange({ user, role: event.target.value as UserRole }); }}>{roles.map(role => <option key={role} value={role}>{t(role)}</option>)}</select></label><button className="text-link" disabled={self || save.isPending} onClick={() => { save.reset(); setChange({ user, active: !user.isActive }); }}>{t(user.isActive ? 'Deactivate' : 'Activate')}</button>{self && <span className="text-xs text-slate-500">{t('Your account')}</span>}</div>;
  }
  return <><div className="page-heading"><div><p className="eyebrow">{t('Administration')}</p><h1>{t('Users')}</h1><p>{t('Manage accounts and access securely.')}</p></div><Link className="button" to="/users/new">{t('Create user')}</Link></div><section className="panel overflow-hidden p-0">
    {!query.data.length ? <p className="p-8">{t('No records yet')}</p> : <><div className="hidden overflow-x-auto md:block"><table><thead><tr>{['Name', 'Email', 'Role', 'Status', 'Created', 'Actions'].map(label => <th key={label}>{t(label)}</th>)}</tr></thead><tbody>{query.data.map(user => <tr key={user.id}><td className="font-semibold">{user.name}</td><td className="break-all">{user.email}</td><td><span className="badge">{t(user.role)}</span></td><td><StatusBadge active={user.isActive} /></td><td>{dateFormat(user.createdAt)}</td><td>{actions(user)}</td></tr>)}</tbody></table></div><ul className="divide-y divide-slate-100 md:hidden">{query.data.map(user => <li key={user.id} className="space-y-3 p-5"><div className="flex justify-between gap-2"><strong className="break-words">{user.name}</strong><StatusBadge active={user.isActive} /></div><p className="break-all text-sm">{user.email}</p><p>{t('Role')}: {t(user.role)} · {dateFormat(user.createdAt)}</p>{actions(user)}</li>)}</ul></>}
  </section>{change && <DeleteConfirmation product={change.user} title="Confirm access change?" description={`${change.user.name} · ${change.role ? `${t('Role')}: ${t(change.role)}` : t(change.active ? 'Activate' : 'Deactivate')}. ${t('Existing sessions will be revoked.')}`} confirmLabel="Confirm change" pending={save.isPending} error={save.error} onCancel={() => setChange(null)} onDelete={() => save.mutate(change)} />}</>;
}

export function UserEditor() {
  const { id } = useParams();
  const query = useQuery({ queryKey: ['users', id], queryFn: ({ signal }) => api<User>(`/users/${id}`, { signal }), enabled: !!id });
  if (id && query.isPending) return <Loading />;
  if (id && query.isError) return <QueryError error={query.error} retry={() => void query.refetch()} back="/users" />;
  return <><Link className="back-link" to="/users">← {t('Back to list')}</Link><div className="page-heading"><h1>{t(id ? 'Edit user' : 'Create user')}</h1></div><UserForm key={id ?? 'new'} value={query.data} /></>;
}

function UserForm({ value }: { value?: User }) {
  const { session, login, logout } = useAuth();
  const schema = z.object({ name: z.string().trim().min(1, 'Enter a name.').max(100, 'Maximum length exceeded.'), email: z.email('Enter a valid email.').max(254, 'Maximum length exceeded.'), password: value ? z.string() : z.string().min(12, 'Use at least 12 characters.').max(128, 'Maximum length exceeded.'), role: z.enum(['Viewer', 'Manager', 'Admin']) });
  type Input = z.infer<typeof schema>;
  const { register, handleSubmit, formState: { errors, isDirty } } = useForm<Input>({ resolver: zodResolver(schema), defaultValues: { name: value?.name ?? '', email: value?.email ?? '', password: '', role: 'Viewer' } });
  const [confirmation, setConfirmation] = useState<Input | null>(null);
  const cache = useQueryClient(); const navigate = useNavigate();
  const save = useMutation({ mutationFn: (input: Input) => api<User>(value ? `/users/${value.id}` : '/users', { method: value ? 'PUT' : 'POST', body: JSON.stringify(value ? { name: input.name, email: input.email } : input) }), onSuccess: async result => {
    if (session && result.id === session.user.id) {
      if (result.email !== session.user.email) { logout(); navigate('/auth/login'); return; }
      login({ ...session, user: result });
    }
    await Promise.all([cache.invalidateQueries({ queryKey: ['users'] }), cache.invalidateQueries({ queryKey: ['audit-logs'] })]); navigate('/users');
  } });
  return <><form className="panel max-w-2xl" noValidate onSubmit={handleSubmit(input => !value && allowed({ role: input.role }, 'users') ? setConfirmation(input) : save.mutate(input))}><fieldset className="space-y-5" disabled={save.isPending}><ErrorNotice error={confirmation ? undefined : save.error} /><label className="field">{t('Name')} *<input maxLength={100} {...register('name')} aria-invalid={!!errors.name} /><FieldError message={errors.name?.message} /></label><label className="field">{t('Email')} *<input type="email" maxLength={254} {...register('email')} aria-invalid={!!errors.email} /><FieldError message={errors.email?.message} /></label>{!value && <><label className="field">{t('Password')} *<input type="password" autoComplete="new-password" maxLength={128} {...register('password')} aria-invalid={!!errors.password} /><FieldError message={errors.password?.message} /></label><label className="field">{t('Role')} *<select {...register('role')}>{roles.map(role => <option key={role} value={role}>{t(role)}</option>)}</select></label></>}<div className="flex justify-end gap-3"><Link className="button secondary" to="/users">{t('Cancel')}</Link><button className="button" disabled={save.isPending || !!value && !isDirty}>{t(save.isPending ? 'Saving…' : value ? 'Save changes' : 'Create user')}</button></div></fieldset></form>{confirmation && <DeleteConfirmation product={{ name: confirmation.name }} title="Create an Admin account?" description="This account will have full administrative access." confirmLabel="Create Admin" pending={save.isPending} error={save.error} onCancel={() => setConfirmation(null)} onDelete={() => save.mutate(confirmation)} />}</>;
}

const actions = ['Create', 'Update', 'Delete', 'Activate', 'Deactivate', 'RoleChange', 'OrderConfirm', 'OrderComplete', 'OrderCancel', 'StockIn', 'StockOut', 'StockAdjustment', 'MinimumLevelChange'];
const entities = ['Product', 'Category', 'Customer', 'Order', 'Inventory', 'User'];
function ActionBadge({ action }: { action: string }) { return <span className={`badge ${action === 'Create' || action === 'Activate' ? 'stock-in' : action === 'Delete' || action === 'Deactivate' ? 'stock-out' : action === 'RoleChange' ? 'stock-low' : 'audit-update'}`}>{t(action)}</span>; }
const time = (date: string) => new Intl.DateTimeFormat(document.documentElement.lang, { dateStyle: 'short', timeStyle: 'short' }).format(new Date(date));

export function AuditPageView() {
  const [filters, setFilters] = useState({ userId: '', action: '', entityName: '', from: '', to: '' });
  const [page, setPage] = useState(1);
  const parameters = new URLSearchParams({ page: String(page), pageSize: '20' });
  for (const [key, value] of Object.entries(filters)) if (value) parameters.set(key, key === 'from' || key === 'to' ? new Date(value).toISOString() : value);
  const users = useQuery({ queryKey: ['users'], queryFn: ({ signal }) => api<User[]>('/users', { signal }) });
  const query = useQuery({ queryKey: ['audit-logs', parameters.toString()], queryFn: ({ signal }) => api<AuditPage>(`/audit-logs?${parameters}`, { signal }) });
  function filter(key: keyof typeof filters, value: string) { setFilters(current => ({ ...current, [key]: value })); setPage(1); }
  return <><div className="page-heading"><div><p className="eyebrow">{t('Administration')}</p><h1>{t('Audit Logs')}</h1><p>{t('Review committed business and access changes.')}</p></div></div><section className="panel mb-6"><div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5"><label className="field">{t('User')}<select aria-label={t('User')} value={filters.userId} onChange={event => filter('userId', event.target.value)}><option value="">{t('All users')}</option>{users.data?.map(user => <option key={user.id} value={user.id}>{user.name}</option>)}</select></label><label className="field">{t('Action')}<select aria-label={t('Action')} value={filters.action} onChange={event => filter('action', event.target.value)}><option value="">{t('All actions')}</option>{actions.map(action => <option key={action} value={action}>{t(action)}</option>)}</select></label><label className="field">{t('Entity')}<select aria-label={t('Entity')} value={filters.entityName} onChange={event => filter('entityName', event.target.value)}><option value="">{t('All entities')}</option>{entities.map(entity => <option key={entity} value={entity}>{t(entity)}</option>)}</select></label><label className="field">{t('From date')}<input type="datetime-local" value={filters.from} onChange={event => filter('from', event.target.value)} /></label><label className="field">{t('To date')}<input type="datetime-local" value={filters.to} onChange={event => filter('to', event.target.value)} /></label></div><button className="text-link mt-4" onClick={() => { setFilters({ userId: '', action: '', entityName: '', from: '', to: '' }); setPage(1); }}>{t('Clear filters')}</button>{users.isError && <ErrorNotice error={users.error} />}</section>
    {query.isPending ? <Loading /> : query.isError ? <QueryError error={query.error} retry={() => void query.refetch()} back="/audit-logs" /> : <><section className="panel overflow-hidden p-0">{!query.data.items.length ? <p className="p-8">{t('No audit records match these filters.')}</p> : <><div className="hidden overflow-x-auto md:block"><table><thead><tr>{['Date', 'User', 'Action', 'Entity', 'Entity ID', 'Description', 'Details'].map(label => <th key={label}>{t(label)}</th>)}</tr></thead><tbody>{query.data.items.map(log => <tr key={log.id}><td className="whitespace-nowrap">{time(log.createdAt)}</td><td>{log.userId ? log.userName : t('System / public registration')}</td><td><ActionBadge action={log.action} /></td><td>{t(log.entityName)}</td><td className="max-w-40 break-all font-mono text-xs">{log.entityId}</td><td>{t(log.description)}</td><td><Link className="text-link" to={`/audit-logs/${log.id}`}>{t('Details')}</Link></td></tr>)}</tbody></table></div><ul className="divide-y divide-slate-100 md:hidden">{query.data.items.map(log => <li key={log.id} className="space-y-3 p-5"><div className="flex flex-wrap justify-between gap-2"><ActionBadge action={log.action} /><span className="text-xs">{time(log.createdAt)}</span></div><p className="break-words">{log.userId ? log.userName : t('System / public registration')} · {t(log.entityName)}</p><p className="break-all font-mono text-xs">{log.entityId}</p><Link className="text-link" to={`/audit-logs/${log.id}`}>{t('Details')}</Link></li>)}</ul></>}</section><div className="mt-5 flex flex-wrap items-center justify-between gap-3"><span className="text-sm">{t('Page')} {page} · {t('Total records')}: {query.data.totalCount}</span><div className="flex gap-3"><button className="button secondary" disabled={page === 1} onClick={() => setPage(value => value - 1)}>{t('Previous')}</button><button className="button secondary" disabled={page * 20 >= query.data.totalCount} onClick={() => setPage(value => value + 1)}>{t('Next')}</button></div></div></>}
  </>;
}

export function AuditDetail() {
  const { id } = useParams();
  const query = useQuery({ queryKey: ['audit-logs', id], queryFn: ({ signal }) => api<AuditLog>(`/audit-logs/${id}`, { signal }) });
  if (query.isPending) return <Loading />;
  if (query.isError) return <QueryError error={query.error} retry={() => void query.refetch()} back="/audit-logs" />;
  const log = query.data;
  const fields = [...new Set([...Object.keys(log.oldValues ?? {}), ...Object.keys(log.newValues ?? {})])];
  const display = (value: unknown, field: string) => value === undefined || value === null ? '—' : typeof value === 'object' ? JSON.stringify(value, null, 2) : typeof value === 'boolean' ? t(value ? 'Active' : 'Inactive') : field === 'role' || field === 'status' ? t(String(value)) : String(value);
  const fieldLabels: Record<string, string> = { name: 'Name', email: 'Email', role: 'Role', isActive: 'Status', price: 'Price', description: 'Description', categoryId: 'Category', status: 'Status', customerId: 'Customer', totalAmount: 'Total', orderNumber: 'Order number', minimumStockLevel: 'Minimum stock level', operation: 'Operation reference' };
  return <><Link className="back-link" to="/audit-logs">← {t('Back to list')}</Link><div className="page-heading"><h1>{t('Audit details')}</h1><ActionBadge action={log.action} /></div><section className="panel"><dl className="detail-grid"><div><dt>{t('User')}</dt><dd>{log.userId ? log.userName : t('System / public registration')}</dd></div><div><dt>{t('Date')}</dt><dd>{time(log.createdAt)}</dd></div><div><dt>{t('Entity')}</dt><dd>{t(log.entityName)}</dd></div><div><dt>{t('Entity ID')}</dt><dd className="break-all font-mono text-xs">{log.entityId}</dd></div><div><dt>{t('Description')}</dt><dd>{t(log.description)}</dd></div><div><dt>{t('Correlation ID')}</dt><dd className="break-all font-mono text-xs">{log.correlationId ?? '—'}</dd></div></dl><h2 className="my-5 text-xl font-semibold">{t('Changed fields')}</h2><div className="overflow-x-auto"><table><thead><tr><th>{t('Field')}</th><th>{t('Before values')}</th><th>{t('After values')}</th></tr></thead><tbody>{fields.map(field => <tr key={field}><td>{t(fieldLabels[field] ?? field)}</td><td className="max-w-72 whitespace-pre-wrap break-words">{display(log.oldValues?.[field], field)}</td><td className="max-w-72 whitespace-pre-wrap break-words">{display(log.newValues?.[field], field)}</td></tr>)}</tbody></table></div></section></>;
}
