import { t } from '../i18n';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api } from '../api';
import { ErrorNotice, FieldError, Loading, QueryError } from '../components';
import type { Product, ProductInput, Category } from '../types';

const schema = z.object({ name: z.string().trim().min(1, t("Enter a product name.")).max(150), description: z.string().trim().max(2000, t("Use no more than 2,000 characters.")),
  price: z.number({ error: t("Enter a valid price.") }).min(0, t("Price cannot be negative.")).max(9999999999.99, t("Price is too large."))
    // Check the same decimal representation sent in JSON, without floating-point rounding.
    .refine(value => /^\d+(?:\.\d{1,2})?$/.test(String(value)), t("Use no more than two decimal places.")), isActive: z.boolean(), categoryId: z.string().min(1, 'Select a category') });

export function ProductEditor() {
  const { id } = useParams();
  const product = useQuery({ queryKey: ['products', id], enabled: !!id, queryFn: ({ signal }) => api<Product>(`/products/${id}`, { signal }) });
  if (id && product.isPending) return <Loading />;
  if (id && product.isError) return <QueryError error={product.error} retry={() => void product.refetch()} />;
  return <>
    <Link className="back-link" to={id ? `/products/${id}` : '/products'}>← {id ? t("Back to product") : t("Back to products")}</Link>
    <div className="page-heading"><div><p className="eyebrow">{t("YOUR CATALOG")}</p><h1>{id ? t("Edit product") : t("New product")}</h1><p>{id ? t("Keep your product details up to date.") : t("Add something new to your catalog.")}</p></div></div>
    <ProductForm key={id ?? 'new'} product={id ? product.data : undefined} />
  </>;
}

function ProductForm({ product }: { product?: Product }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const categories = useQuery({ queryKey: ['categories'], queryFn: ({ signal }) => api<Category[]>('/categories', { signal }) });
  const { register, handleSubmit, formState: { errors, isDirty } } = useForm<ProductInput>({ resolver: zodResolver(schema),
    defaultValues: product ? { name: product.name, description: product.description, price: product.price, isActive: product.isActive, categoryId: product.categoryId }
      : { name: '', description: '', price: 0, isActive: true, categoryId: '' } });
  const mutation = useMutation({ mutationFn: (value: ProductInput) => api<Product>(product ? `/products/${product.id}` : '/products', { method: product ? 'PUT' : 'POST', body: JSON.stringify(value) }),
    onSuccess: async value => {
      queryClient.setQueryData(['products', value.id], value);
      await queryClient.invalidateQueries({ queryKey: ['products'] });
      await queryClient.invalidateQueries({ queryKey: ['categories'] });
      navigate(`/products/${value.id}`);
    } });
  return <form className="panel max-w-3xl" noValidate onSubmit={handleSubmit(value => mutation.mutate(value))}>
    <div className="mb-6 border-b border-slate-100 pb-5"><h2 className="font-semibold">{t("Product information")}</h2><p className="mt-1 text-sm text-slate-500">{t("Fields marked with * are required.")}</p></div>
    <fieldset className="space-y-6" disabled={mutation.isPending}>
      <ErrorNotice error={mutation.error} />
      {categories.isPending ? <p role="status">{t('Loading…')}</p> : categories.isError ? <ErrorNotice error={categories.error} /> : <label className="field">{t('Category')} *<select {...register('categoryId')} aria-invalid={!!errors.categoryId}><option value="">{t('Select a category')}</option>{categories.data.filter(c => c.isActive || c.id === product?.categoryId).map(c => <option key={c.id} value={c.id}>{c.name}{c.isActive ? '' : ` (${t('Inactive')})`}</option>)}</select><FieldError message={errors.categoryId?.message} /></label>}
      {categories.data?.length === 0 && <Link className="text-link" to="/categories/new">{t('Create a category first.')}</Link>}
      <label className="field">{t("Product name *")}<input autoFocus maxLength={150} placeholder={t("e.g. Studio notebook")} {...register('name')} aria-invalid={!!errors.name} /><FieldError message={errors.name?.message} /></label>
      <label className="field">{t("Description")}<textarea rows={5} maxLength={2000} placeholder={t("A few details about this product…")} {...register('description')} aria-invalid={!!errors.description} /><span className="text-xs font-normal text-slate-500">{t("Optional · Up to 2,000 characters.")}</span><FieldError message={errors.description?.message} /></label>
      <label className="field max-w-xs">{t("Price (USD) *")}<input type="number" min="0" step="0.01" {...register('price', { valueAsNumber: true })} aria-invalid={!!errors.price} /><FieldError message={errors.price?.message} /></label>
      <label className="flex cursor-pointer items-start gap-3 rounded-lg border border-slate-200 bg-slate-50 p-4"><input className="mt-1 size-4 accent-emerald-800" type="checkbox" {...register('isActive')} /><span><span className="block text-sm font-medium">{t("Active product")}</span><span className="mt-1 block text-sm text-slate-500">{t("Inactive products stay in your catalog and can be activated later.")}</span></span></label>
      <div className="flex justify-end gap-3 border-t border-slate-100 pt-6"><Link className="button secondary" to={product ? `/products/${product.id}` : '/products'}>{t("Cancel")}</Link><button className="button" disabled={mutation.isPending || (!!product && !isDirty)}>{mutation.isPending ? t("Saving…") : product ? t("Save changes") : t("Create product")}</button></div>
    </fieldset>
  </form>;
}
