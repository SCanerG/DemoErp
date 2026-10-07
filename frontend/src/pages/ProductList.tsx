import { t } from '../i18n';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api';
import { dateFormat, Loading, priceFormat, QueryError, StatusBadge } from '../components';
import { DeleteConfirmation } from '../DeleteConfirmation';
import type { Product } from '../types';

export function ProductList() {
  const queryClient = useQueryClient();
  const [deleting, setDeleting] = useState<Product | null>(null);
  const products = useQuery({ queryKey: ['products'], queryFn: ({ signal }) => api<Product[]>('/products', { signal }) });
  const mutation = useMutation({ mutationFn: (id: string) => api<void>(`/products/${id}`, { method: 'DELETE' }),
    onSuccess: async (_, id) => { queryClient.removeQueries({ queryKey: ['products', id] }); await queryClient.invalidateQueries({ queryKey: ['products'] }); setDeleting(null); } });
  if (products.isPending) return <Loading />;
  if (products.isError) return <QueryError error={products.error} retry={() => void products.refetch()} />;
  const activeCount = products.data.filter(p => p.isActive).length;
  const confirmDelete = (product: Product) => { mutation.reset(); setDeleting(product); };
  return <>
    <div className="page-heading"><div><p className="eyebrow">{t("YOUR WORKSPACE")}</p><h1>{t("Products")}</h1><p>{t("A home for everything in your catalog.")}</p></div>
      <Link className="button" to="/products/new"><span aria-hidden="true">＋</span> {t("New product")}</Link></div>
    <div className="stats-grid">
      <div className="stat"><span>{t("Total products")}</span><strong>{products.data.length}<span className="stat-icon" aria-hidden="true">▦</span></strong></div>
      <div className="stat"><span>{t("Active products")}</span><strong>{activeCount}<span className="stat-icon text-emerald-700" aria-hidden="true">↗</span></strong></div>
      <div className="stat"><span>{t("Inactive products")}</span><strong>{products.data.length - activeCount}<span className="stat-icon" aria-hidden="true">○</span></strong></div>
    </div>
    <section className="panel overflow-hidden p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-6 py-5"><h2 className="font-semibold">{t("All products")} <span className="ml-2 text-sm font-normal text-slate-400">{products.data.length}</span></h2><span className="text-xs text-slate-500">{t("Latest additions first")}</span></div>
      {products.data.length === 0 ? <div className="px-6 py-16 text-center"><div className="empty-icon" aria-hidden="true">▦</div><h2 className="mt-5 text-xl font-semibold">{t("Your catalog is a clean slate")}</h2><p className="mb-6 mt-2 text-slate-500">{t("Add your first product to bring it to life.")}</p><Link className="button" to="/products/new">{t("Create your first product")}</Link></div>
        : <><div className="hidden overflow-x-auto md:block"><table><thead><tr><th>{t("Product name")}</th><th>{t("Category")}</th><th>{t("Price")}</th><th>{t("Status")}</th><th>{t("Created")}</th><th className="text-right">{t("Actions")}</th></tr></thead>
          <tbody>{products.data.map(product => <tr key={product.id}>
            <td><Link className="font-semibold hover:text-emerald-700" to={`/products/${product.id}`}>{product.name}</Link><p className="mt-1 max-w-64 truncate text-xs text-slate-500">{product.description || t("No description")}</p></td>
            <td>{product.categoryName}</td><td className="whitespace-nowrap tabular-nums">{priceFormat.format(product.price)}</td><td><StatusBadge active={product.isActive} /></td><td className="whitespace-nowrap text-slate-500">{dateFormat(product.createdAt)}</td>
            <td><ProductActions product={product} onDelete={() => confirmDelete(product)} /></td>
          </tr>)}</tbody></table></div>
          <ul className="divide-y divide-slate-100 md:hidden" aria-label={t("Products")}>
            {products.data.map(product => <li key={product.id} className="px-5 py-5">
              <div className="flex items-start justify-between gap-3"><Link className="min-w-0 break-words text-base font-semibold hover:text-emerald-700" to={`/products/${product.id}`}>{product.name}</Link><StatusBadge active={product.isActive} /></div>
              <p className="mt-2 text-xs text-blue-700">{t("Category")}: {product.categoryName}</p><p className="mt-2 line-clamp-2 break-words text-sm leading-6 text-slate-500">{product.description || t("No description")}</p>
              <div className="mb-5 mt-4 flex items-center justify-between gap-3"><span className="text-lg font-semibold tabular-nums">{priceFormat.format(product.price)}</span><span className="text-xs text-slate-500">{dateFormat(product.createdAt)}</span></div>
              <ProductActions product={product} onDelete={() => confirmDelete(product)} mobile />
            </li>)}
          </ul></>}
    </section>
    <p className="mt-5 text-xs text-slate-400">{t("Your shared catalog · Changes are saved when you submit.")}</p>
    {deleting && <DeleteConfirmation product={deleting} pending={mutation.isPending} error={mutation.error}
      onCancel={() => setDeleting(null)} onDelete={() => mutation.mutate(deleting.id)} />}
  </>;
}

function ProductActions({ product, onDelete, mobile = false }: { product: Product; onDelete: () => void; mobile?: boolean }) {
  return <div className={mobile ? 'mobile-product-actions' : 'flex justify-end gap-4'}>
    <Link className="text-link" to={`/products/${product.id}`}>{t("View")}</Link>
    <Link className="text-link" to={`/products/${product.id}/edit`}>{t("Edit")}</Link>
    <button className="text-red-600 hover:underline" aria-label={`${t("Delete")} ${product.name}`} onClick={onDelete}>{t("Delete")}</button>
  </div>;
}
