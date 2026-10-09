import { Can } from '../permissions';
import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { api } from '../api';
import { t } from '../i18n';
import { dateFormat, ErrorNotice, FieldError, Loading, QueryError, StatusBadge } from '../components';
import { DeleteConfirmation } from '../DeleteConfirmation';
import type { Category, Customer } from '../types';

type Kind = 'categories' | 'customers';
type RecordValue = Category | Customer;
const labels = { categories: { list: 'Categories', create: 'Create category', new: 'New category', edit: 'Edit category', detail: 'Category details', count: 'Product count' }, customers: { list: 'Customers', create: 'Create customer', new: 'New customer', edit: 'Edit customer', detail: 'Customer details', count: 'Order count' } };
function count(value: RecordValue) { return 'productCount' in value ? value.productCount : value.orderCount; }
export function RecordList({ kind }: { kind: Kind }) {
  const cache = useQueryClient();
  const [deleting, setDeleting] = useState<RecordValue | null>(null);
  const query = useQuery({ queryKey: [kind], queryFn: ({ signal }) => api<RecordValue[]>(`/${kind}`, { signal }) });
  const remove = useMutation({ mutationFn: (id: string) => api<void>(`/${kind}/${id}`, { method: 'DELETE' }), onSuccess: async (_, id) => { cache.removeQueries({ queryKey: [kind, id] }); await cache.invalidateQueries({ queryKey: [kind] }); setDeleting(null); } });
  if (query.isPending) return <Loading />;
  if (query.isError) return <QueryError error={query.error} retry={() => void query.refetch()} back={`/${kind}`} />;
  return <>
    <div className="page-heading"><div><p className="eyebrow">{t('YOUR WORKSPACE')}</p><h1>{t(labels[kind].list)}</h1><p>{t('Manage your business records.')}</p></div><Can permission="write"><Link className="button" to={`/${kind}/new`}>＋ {t(labels[kind].new)}</Link></Can></div>
    <div className="stats-grid"><div className="stat"><span>{t(kind === 'customers' ? 'Total customers' : 'Total categories')}</span><strong>{query.data.length}</strong></div><div className="stat accent-green"><span>{t('Active')}</span><strong>{query.data.filter(v => v.isActive).length}</strong></div><div className="stat accent-amber"><span>{t('Inactive')}</span><strong>{query.data.filter(v => !v.isActive).length}</strong></div></div>
    <section className="panel p-0 overflow-hidden">{query.data.length === 0 ? <div className="p-10 text-center"><h2 className="text-xl font-semibold">{t('No records yet')}</h2><p className="mt-2 text-slate-500">{t('Create your first record to get started.')}</p></div> : <>
      <div className="hidden overflow-x-auto md:block"><table><thead><tr><th>{t('Name')}</th>{kind === 'customers' && <><th>{t('Email')}</th><th>{t('Phone')}</th></>}<th>{t('Status')}</th><th>{t(labels[kind].count)}</th><th>{t('Created')}</th><th>{t('Actions')}</th></tr></thead><tbody>{query.data.map(value => <tr key={value.id}><td><Link className="text-link" to={`/${kind}/${value.id}`}>{value.name}</Link></td>{'email' in value && <><td>{value.email || '—'}</td><td>{value.phone || '—'}</td></>}<td><StatusBadge active={value.isActive} /></td><td>{count(value)}</td><td>{dateFormat(value.createdAt)}</td><td><RecordActions kind={kind} value={value} remove={() => { remove.reset(); setDeleting(value); }} /></td></tr>)}</tbody></table></div>
      <ul className="divide-y divide-slate-100 md:hidden">{query.data.map(value => <li className="p-5" key={value.id}><div className="flex justify-between gap-2"><Link className="text-link break-words" to={`/${kind}/${value.id}`}>{value.name}</Link><StatusBadge active={value.isActive} /></div>{'email' in value && <p className="my-3 break-all text-sm text-slate-500">{value.email} · {value.phone}</p>}<p className="my-3 text-sm text-slate-500">{t(labels[kind].count)}: {count(value)} · {dateFormat(value.createdAt)}</p><RecordActions kind={kind} value={value} remove={() => { remove.reset(); setDeleting(value); }} /></li>)}</ul>
    </>}</section>
    {deleting && <DeleteConfirmation record product={deleting} pending={remove.isPending} error={remove.error} onCancel={() => setDeleting(null)} onDelete={() => remove.mutate(deleting.id)} />}
  </>;
}
function RecordActions({ kind, value, remove }: { kind: Kind; value: RecordValue; remove: () => void }) { return <div className="flex gap-4"><Link className="text-link" to={`/${kind}/${value.id}`}>{t('View')}</Link><Can permission="write"><Link className="text-link" to={`/${kind}/${value.id}/edit`}>{t('Edit')}</Link></Can><Can permission="delete"><button className="text-red-700" aria-label={`${t('Delete')} ${value.name}`} onClick={remove}>{t('Delete')}</button></Can></div>; }

export function RecordDetail({ kind }: { kind: Kind }) {
  const { id } = useParams();
  const query = useQuery({ queryKey: [kind, id], queryFn: ({ signal }) => api<RecordValue>(`/${kind}/${id}`, { signal }) });
  if (query.isPending) return <Loading />;
  if (query.isError) return <QueryError error={query.error} retry={() => void query.refetch()} back={`/${kind}`} />;
  const value = query.data;
  return <><Link className="back-link" to={`/${kind}`}>← {t('Back to list')}</Link><div className="page-heading"><div><p className="eyebrow">{t(labels[kind].detail)}</p><h1 className="break-words">{value.name}</h1></div><Can permission="write"><Link className="button" to={`/${kind}/${id}/edit`}>{t(labels[kind].edit)}</Link></Can></div><section className="panel max-w-3xl"><StatusBadge active={value.isActive} /><dl className="detail-grid">{'description' in value ? <div><dt>{t('Description')}</dt><dd className="whitespace-pre-wrap break-words">{value.description || t('No description')}</dd></div> : <>{(['email', 'phone', 'address'] as const).map(field => <div key={field}><dt>{t(field === 'email' ? 'Email' : field === 'phone' ? 'Phone' : 'Address')}</dt><dd className="whitespace-pre-wrap break-words">{value[field] || '—'}</dd></div>)}</>}<div><dt>{t(labels[kind].count)}</dt><dd>{count(value)}</dd></div><div><dt>{t('Created')}</dt><dd>{dateFormat(value.createdAt)}</dd></div><div><dt>{t('Last updated')}</dt><dd>{value.updatedAt ? dateFormat(value.updatedAt) : t('Not updated yet')}</dd></div></dl></section></>;
}

const schema = z.object({ name: z.string().trim().min(1, 'Enter a name.').max(150, 'Maximum length exceeded.'), description: z.string().trim().max(2000, 'Maximum length exceeded.'), email: z.string().trim().max(254, 'Maximum length exceeded.').refine(value => !value || z.email().safeParse(value).success, 'Enter a valid email address.'), phone: z.string().trim().max(40, 'Maximum length exceeded.'), address: z.string().trim().max(1000, 'Maximum length exceeded.'), isActive: z.boolean() });
type Values = z.infer<typeof schema>;
export function RecordEditor({ kind }: { kind: Kind }) {
  const { id } = useParams();
  const query = useQuery({ queryKey: [kind, id], enabled: !!id, queryFn: ({ signal }) => api<RecordValue>(`/${kind}/${id}`, { signal }) });
  if (id && query.isPending) return <Loading />;
  if (id && query.isError) return <QueryError error={query.error} retry={() => void query.refetch()} back={`/${kind}`} />;
  return <><Link className="back-link" to={`/${kind}`}>← {t('Back to list')}</Link><div className="page-heading"><div><p className="eyebrow">{t('YOUR WORKSPACE')}</p><h1>{t(id ? labels[kind].edit : labels[kind].new)}</h1></div></div><RecordForm key={`${kind}:${id ?? 'new'}`} kind={kind} value={id ? query.data : undefined} /></>;
}
function RecordForm({ kind, value }: { kind: Kind; value?: RecordValue }) {
  const navigate = useNavigate(); const cache = useQueryClient();
  const { register, handleSubmit, formState: { errors, isDirty } } = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { name: value?.name ?? '', description: value && 'description' in value ? value.description : '', email: value && 'email' in value ? value.email : '', phone: value && 'phone' in value ? value.phone : '', address: value && 'address' in value ? value.address : '', isActive: value?.isActive ?? true } });
  const save = useMutation({ mutationFn: (input: Values) => api<RecordValue>(value ? `/${kind}/${value.id}` : `/${kind}`, { method: value ? 'PUT' : 'POST', body: JSON.stringify(kind === 'categories' ? { name: input.name, description: input.description, isActive: input.isActive } : { name: input.name, email: input.email, phone: input.phone, address: input.address, isActive: input.isActive }) }), onSuccess: async result => { cache.setQueryData([kind, result.id], result); await cache.invalidateQueries({ queryKey: [kind] }); await cache.invalidateQueries({ queryKey: [kind === 'categories' ? 'products' : 'orders'] }); if (kind === 'categories') await cache.invalidateQueries({ queryKey: ['inventory'] }); navigate(`/${kind}/${result.id}`); } });
  return <form className="panel max-w-3xl" noValidate onSubmit={handleSubmit(input => save.mutate(input))}><fieldset className="space-y-6" disabled={save.isPending}><ErrorNotice error={save.error} /><p className="text-sm text-slate-500">{t('Fields marked with * are required.')}</p><label className="field">{t('Name')} *<input autoFocus maxLength={150} {...register('name')} aria-invalid={!!errors.name} /><FieldError message={errors.name?.message} /></label>
    {kind === 'categories' ? <label className="field">{t('Description')}<textarea rows={4} maxLength={2000} {...register('description')} /><FieldError message={errors.description?.message} /></label> : <>{(['email', 'phone', 'address'] as const).map(field => <label key={field} className="field">{t(field === 'email' ? 'Email' : field === 'phone' ? 'Phone' : 'Address')}{field === 'address' ? <textarea rows={3} maxLength={1000} {...register(field)} /> : <input type={field === 'email' ? 'email' : 'tel'} maxLength={field === 'email' ? 254 : 40} {...register(field)} aria-invalid={!!errors[field]} />}<FieldError message={errors[field]?.message} /></label>)}</>}
    <label className="flex items-center gap-3 rounded-lg bg-emerald-50 p-4"><input type="checkbox" {...register('isActive')} />{t('Active')}</label><div className="flex justify-end gap-3"><Link className="button secondary" to={`/${kind}`}>{t('Cancel')}</Link><button className="button" disabled={save.isPending || (!!value && !isDirty)}>{t(save.isPending ? 'Saving…' : value ? 'Save changes' : labels[kind].create)}</button></div></fieldset></form>;
}
