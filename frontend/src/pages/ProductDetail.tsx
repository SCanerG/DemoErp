import { Can } from '../permissions';
import { t } from '../i18n';
import { Link, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api';
import { dateFormat, Loading, priceFormat, QueryError, StatusBadge } from '../components';
import type { Product } from '../types';

export function ProductDetail() {
  const { id } = useParams();
  const product = useQuery({ queryKey: ['products', id], queryFn: ({ signal }) => api<Product>(`/products/${id}`, { signal }) });
  if (product.isPending) return <Loading />;
  if (product.isError) return <QueryError error={product.error} retry={() => void product.refetch()} />;
  const value = product.data;
  return <>
    <Link className="back-link" to="/products">{t("← Back to products")}</Link>
    <div className="page-heading"><div><p className="eyebrow">{t("PRODUCT DETAILS")}</p><h1 className="break-words">{value.name}</h1><p>{t("Everything you need to know, in one place.")}</p></div><Can permission="write"><Link className="button" to={`/products/${id}/edit`}>{t("Edit product")}</Link></Can></div>
    <section className="panel max-w-3xl"><div className="flex items-center justify-between border-b border-slate-100 pb-6"><h2 className="font-semibold">{t("Product overview")}</h2><StatusBadge active={value.isActive} /></div>
      <dl className="detail-grid"><div><dt>{t("Category")}</dt><dd><Link className="text-link" to={`/categories/${value.categoryId}`}>{value.categoryName}</Link></dd></div><div><dt>{t("Price")}</dt><dd className="text-3xl font-semibold">{priceFormat.format(value.price)}</dd></div><div><dt>{t("Product ID")}</dt><dd className="break-all font-mono text-xs">{value.id}</dd></div>
        <div className="sm:col-span-2"><dt>{t("Description")}</dt><dd className="whitespace-pre-wrap break-words leading-7">{value.description || t("No description provided.")}</dd></div>
        <div><dt>{t("Created")}</dt><dd>{dateFormat(value.createdAt)}</dd></div><div><dt>{t("Last updated")}</dt><dd>{value.updatedAt ? dateFormat(value.updatedAt) : t("Not updated yet")}</dd></div></dl>
      <Link className="button secondary" to={`/inventory/${id}`}>{t('View inventory')}</Link>
    </section>
  </>;
}
