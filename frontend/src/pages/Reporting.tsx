import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, NavLink, useParams } from 'react-router-dom';
import { Area, AreaChart, Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { api } from '../api';
import { ErrorNotice, Loading, dateFormat, priceFormat } from '../components';
import { t, getLanguage } from '../i18n';
import type { Category, Customer, Product } from '../types';

type Range = { start: string; end: string };
type Row = Record<string, string | number | null>;
type Page = { items: Row[]; page: number; pageSize: number; totalCount: number; totalPages: number };
type Summary = { totalRevenue: number; totalOrders: number; completedOrders: number; pendingOrders: number; activeCustomers: number; activeProducts: number; lowStockProducts: number; outOfStockProducts: number; legacyCompletedOrders: number };
const kinds = ['sales', 'products', 'customers', 'inventory'] as const;
type Kind = typeof kinds[number];
const names = { sales: 'Sales report', products: 'Product performance', customers: 'Customer report', inventory: 'Inventory report' };
const localDate = (date: Date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
function preset(days: number | 'year'): Range {
  const now = new Date(); const end = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1);
  const start = days === 'year' ? new Date(now.getFullYear(), 0, 1) : new Date(now.getFullYear(), now.getMonth(), now.getDate() - days + 1);
  return { start: localDate(start), end: localDate(new Date(end.getFullYear(), end.getMonth(), end.getDate() - 1)) };
}
export function utcRange(range: Range) {
  // Parsing components constructs local midnight; adding a calendar day handles DST correctly.
  const date = (value: string, next = false) => { const [y, m, d] = value.split('-').map(Number); return new Date(y, m - 1, d + (next ? 1 : 0)).toISOString(); };
  return { start: date(range.start), end: date(range.end, true) };
}
function DateRange({ value, onChange }: { value: Range; onChange: (value: Range) => void }) {
  const [selection, setSelection] = useState('30');
  return <div className="report-dates"><label>{t('Reporting period')}<select aria-label={t('Reporting period')} value={selection} onChange={e => { setSelection(e.target.value); if (e.target.value !== 'custom') onChange(preset(e.target.value === 'year' ? 'year' : Number(e.target.value))); }}>
    {[['7', 'Last 7 days'], ['30', 'Last 30 days'], ['90', 'Last 90 days'], ['year', 'This year'], ['custom', 'Custom date range']].map(([v, label]) => <option key={v} value={v}>{t(label)}</option>)}</select></label>
    <label>{t('Start date')}<input type="date" required value={value.start} onChange={e => { if (e.target.value) { setSelection('custom'); onChange({ ...value, start: e.target.value }); } }} /></label>
    <label>{t('End date')}<input type="date" required value={value.end} onChange={e => { if (e.target.value) { setSelection('custom'); onChange({ ...value, end: e.target.value }); } }} /></label></div>;
}
function number(value: number) { return new Intl.NumberFormat(getLanguage() === 'tr' ? 'tr-TR' : 'en-US').format(value); }
function Empty() { return <p className="py-10 text-center text-slate-500">{t('No results for these filters.')}</p>; }
const productColumns = [['product', 'Product'], ['category', 'Category'], ['quantitySold', 'Quantity sold'], ['salesValue', 'Completed sales'], ['completedOrderCount', 'Completed orders'], ['currentStock', 'Current stock']];
const customerColumns = [['customer', 'Customer'], ['totalOrders', 'Orders created in period'], ['completedOrders', 'Completed orders'], ['salesValue', 'Completed sales'], ['averageOrderValue', 'Average completed order'], ['lastCompletedAt', 'Last completion']];
const columns: Record<Kind, string[][]> = {
  sales: [['orderNumber', 'Order number'], ['customer', 'Customer'], ['orderDate', 'Order date'], ['completedAt', 'Completion date'], ['status', 'Status'], ['itemCount', 'Item count'], ['totalAmount', 'Total amount']],
  products: productColumns, customers: customerColumns,
  inventory: [['product', 'Product'], ['category', 'Category'], ['quantityOnHand', 'Quantity on hand'], ['minimumStockLevel', 'Minimum stock level'], ['stockStatus', 'Stock status'], ['updatedAt', 'Last updated']]
};
function cell(key: string, value: string | number | null) {
  if (value === null) return '—';
  if (['salesValue', 'averageOrderValue', 'totalAmount'].includes(key)) return priceFormat.format(Number(value));
  if (['orderDate', 'completedAt', 'lastCompletedAt', 'updatedAt'].includes(key)) return dateFormat(String(value));
  if (key === 'status' || key === 'stockStatus') return <span className={`report-status status-${value}`}>{t(String(value))}</span>;
  return typeof value === 'number' ? number(value) : value;
}
function Table({ rows, fields }: { rows: Row[]; fields: string[][] }) {
  return rows.length === 0 ? <Empty /> : <div className="overflow-x-auto" role="region" aria-label={t('Report data')} tabIndex={0}><table><thead><tr>{fields.map(([key, label]) => <th key={key}>{t(label)}</th>)}</tr></thead><tbody>{rows.map(row => <tr key={String(row.id)}>{fields.map(([key]) => <td key={key}>{cell(key, row[key])}</td>)}</tr>)}</tbody></table></div>;
}
export function DashboardPage() {
  const [range, setRange] = useState<Range>(() => preset(30));
  const dates = utcRange(range); const params = new URLSearchParams(dates).toString();
  const data = useQuery({ queryKey: ['dashboard', dates], queryFn: async ({ signal }) => {
    const [summary, trend, statuses, products, customers, alerts] = await Promise.all([
      api<Summary>(`/dashboard/summary?${params}`, { signal }),
      api<{ granularity: string; items: { period: string; salesValue: number }[] }>(`/dashboard/sales-trend?${params}`, { signal }),
      api<{ status: string; count: number }[]>(`/dashboard/order-status?${params}`, { signal }),
      api<Row[]>(`/dashboard/top-products?${params}`, { signal }), api<Row[]>(`/dashboard/top-customers?${params}`, { signal }),
      api<Page>('/reports/inventory?stockStatus=Low&pageSize=5&sort=quantityOnHand&descending=false', { signal })
    ]); return { summary, trend, statuses, products, customers, alerts };
  } });
  const kpis: [keyof Summary, string, boolean][] = [['totalRevenue', 'Completed-order sales value', false], ['totalOrders', 'Total orders', false], ['completedOrders', 'Completed orders', false], ['pendingOrders', 'Pending orders', false], ['activeCustomers', 'Active customers', true], ['activeProducts', 'Active products', true], ['lowStockProducts', 'Low stock products', true], ['outOfStockProducts', 'Out of stock products', true]];
  const colors: Record<string, string> = { Pending: '#b45309', Confirmed: '#2563eb', Completed: '#059669', Cancelled: '#64748b' };
  const result = data.data;
  return <><div className="page-heading"><div><p className="eyebrow">{t('Business overview')}</p><h1>{t('Dashboard')}</h1><p>{t('A clear view of sales and current operations.')}</p></div><Link className="button secondary" to="/reports/sales">{t('View reports')}</Link></div>
    <section className="panel mb-6"><DateRange value={range} onChange={setRange} /><p className="report-note">{t('Sales use completion dates. Order counts use creation dates. Dates on charts are UTC.')}</p></section>
    {data.isPending ? <Loading /> : data.isError ? <><ErrorNotice error={data.error} /><button className="button" onClick={() => void data.refetch()}>{t('Try again')}</button></> : result && <>
      <div className="kpi-grid">{kpis.map(([key, label, current]) => <article className="kpi-card" key={key}><p>{t(label)}</p><strong>{key === 'totalRevenue' ? priceFormat.format(result.summary[key]) : number(result.summary[key])}</strong><span>{t(current ? 'Current snapshot' : 'Selected period')}</span></article>)}</div>
      {result.summary.legacyCompletedOrders > 0 && <p className="notice mt-6">{t('Legacy completed orders without a completion date are excluded from period sales.')} {number(result.summary.legacyCompletedOrders)}</p>}
      <div className="dashboard-charts"><section className="panel min-w-0"><h2>{t('Sales trend')}</h2><p className="report-note">{t('Completed-order sales value')}{' · USD · '}{t(result.trend.granularity === 'month' ? 'Monthly' : 'Daily')}</p>
        {result.trend.items.every(x => x.salesValue === 0) ? <Empty /> : <div className="chart-box" role="img" aria-label={t('Sales trend')}><ResponsiveContainer width="100%" height="100%"><AreaChart data={result.trend.items} margin={{ left: 5, right: 12, top: 20, bottom: 25 }}><CartesianGrid strokeDasharray="3 3" vertical={false} /><XAxis dataKey="period" tickFormatter={v => new Date(v).toLocaleDateString(getLanguage() === 'tr' ? 'tr-TR' : 'en-US', { timeZone: 'UTC', month: 'short', day: result.trend.granularity === 'day' ? 'numeric' : undefined })} label={{ value: t('Completion date (UTC)'), position: 'insideBottom', offset: -15 }} /><YAxis width={70} tickFormatter={number} label={{ value: 'USD', angle: -90, position: 'insideLeft' }} /><Tooltip formatter={v => priceFormat.format(Number(v))} labelFormatter={v => new Date(String(v)).toLocaleDateString(getLanguage() === 'tr' ? 'tr-TR' : 'en-US', { timeZone: 'UTC' })} /><Area name={t('Completed sales')} type="monotone" dataKey="salesValue" stroke="#047857" fill="#d1fae5" isAnimationActive={false} /></AreaChart></ResponsiveContainer></div>}
        <details className="mt-3"><summary>{t('View chart data')}</summary><div className="max-h-48 overflow-auto"><table><thead><tr><th>{t('Completion date (UTC)')}</th><th>{t('Completed sales')}</th></tr></thead><tbody>{result.trend.items.map(x => <tr key={x.period}><td>{new Date(x.period).toLocaleDateString(getLanguage() === 'tr' ? 'tr-TR' : 'en-US', { timeZone: 'UTC' })}</td><td>{priceFormat.format(x.salesValue)}</td></tr>)}</tbody></table></div></details></section>
        <section className="panel min-w-0"><h2>{t('Orders by status')}</h2><p className="report-note">{t('Current status of orders created in the selected period.')}</p><div className="chart-box" role="img" aria-label={t('Orders by status')}><ResponsiveContainer width="100%" height="100%"><BarChart data={result.statuses.map(x => ({ ...x, label: t(x.status) }))} margin={{ top: 20, bottom: 20, right: 10 }}><CartesianGrid strokeDasharray="3 3" vertical={false} /><XAxis dataKey="label" tick={{ fontSize: 11 }} /><YAxis allowDecimals={false} width={40} /><Tooltip formatter={v => number(Number(v))} /><Bar name={t('Orders')} dataKey="count" label={{ position: 'top' }} isAnimationActive={false}>{result.statuses.map(x => <Cell key={x.status} fill={colors[x.status]} />)}</Bar></BarChart></ResponsiveContainer></div><ul className="status-legend">{result.statuses.map(x => <li key={x.status}><span style={{ background: colors[x.status] }} aria-hidden="true" />{t(x.status)} <strong>{number(x.count)}</strong></li>)}</ul></section></div>
      <section className="panel mt-6"><h2>{t('Top 10 selling products')}</h2><p className="report-note">{t('Stored sale prices · current names and stock')}</p><Table rows={result.products} fields={productColumns} /></section>
      <section className="panel mt-6"><h2>{t('Top customers')}</h2><Table rows={result.customers} fields={customerColumns.filter(([key]) => key !== 'totalOrders' && key !== 'lastCompletedAt')} /></section>
      <section className="panel mt-6"><div className="flex flex-wrap justify-between gap-3"><h2>{t('Low stock alerts')}</h2><Link to="/reports/inventory">{t('View current inventory')}</Link></div><Table rows={result.alerts.items} fields={columns.inventory} /></section>
    </>}
  </>;
}
export function ReportsPage() {
  const { kind: requested } = useParams(); const kind: Kind = kinds.includes(requested as Kind) ? requested as Kind : 'sales';
  const [range, setRange] = useState<Range>(() => preset(30)); const [filters, setFilters] = useState<Record<string, string>>({});
  const [page, setPage] = useState(1); const [pageSize, setPageSize] = useState(20); const [exporting, setExporting] = useState(false); const [exportError, setExportError] = useState<unknown>(null);
  const params = new URLSearchParams({ ...(kind === 'inventory' ? {} : utcRange(range)), ...filters, page: String(page), pageSize: String(pageSize) }).toString();
  const data = useQuery({ queryKey: ['reports', kind, params], queryFn: ({ signal }) => api<Page>(`/reports/${kind}?${params}`, { signal }) });
  const categories = useQuery({ queryKey: ['categories'], queryFn: ({ signal }) => api<Category[]>('/categories', { signal }), enabled: kind === 'products' || kind === 'inventory' });
  const customers = useQuery({ queryKey: ['customers'], queryFn: ({ signal }) => api<Customer[]>('/customers', { signal }), enabled: kind === 'sales' || kind === 'customers' });
  const products = useQuery({ queryKey: ['products'], queryFn: ({ signal }) => api<Product[]>('/products', { signal }), enabled: kind === 'products' });
  function filter(key: string, value: string) { setPage(1); setFilters(old => { const next = { ...old }; if (value) next[key] = value; else delete next[key]; return next; }); }
  const select = (key: string, label: string, options: { id: string; name: string }[]) => <label>{t(label)}<select aria-label={t(label)} value={filters[key] ?? (key === 'sort' || key === 'descending' ? options[0]?.id : '')} onChange={e => filter(key, e.target.value)}>{key !== 'sort' && key !== 'descending' && <option value="">{t('All')}</option>}{options.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>;
  async function download() {
    setExporting(true); setExportError(null);
    try { const blob = await api<Blob>(`/reports/${kind}/export?${params}`, {}, 'blob'); const url = URL.createObjectURL(blob); const a = document.createElement('a'); a.href = url; a.download = `${kind}.csv`; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
    catch (error) { setExportError(error); } finally { setExporting(false); }
  }
  const sorts: Record<Kind, string[]> = { sales: ['orderDate', 'completedAt', 'totalAmount', 'customer', 'orderNumber'], products: ['salesValue', 'quantitySold', 'product', 'currentStock'], customers: ['salesValue', 'customer', 'totalOrders', 'completedOrders'], inventory: ['product', 'quantityOnHand', 'minimumStockLevel', 'updatedAt'] };
  const sortLabels: Record<string, string> = Object.fromEntries(Object.values(columns).flat().map(([key, label]) => [key, label]));
  return <><div className="page-heading"><div><p className="eyebrow">{t('Reports')}</p><h1>{t(names[kind])}</h1><p>{t(kind === 'inventory' ? 'Current inventory snapshot. Date filters do not apply.' : kind === 'sales' ? 'Orders filtered by creation date; includes every selected status.' : 'Completed sales filtered by completion date. Current names are shown.')}</p></div><button className="button secondary" disabled={exporting || data.isPending || data.isError} onClick={() => void download()}>{t(exporting ? 'Exporting…' : 'Export CSV')}</button></div>
    <nav className="report-tabs" aria-label={t('Reports')}>{kinds.map(k => <NavLink key={k} className={({ isActive }) => isActive ? 'selected' : ''} to={`/reports/${k}`} onClick={() => { setFilters({}); setPage(1); setExportError(null); }}>{t(names[k])}</NavLink>)}</nav>
    <section className="panel mb-6">{kind !== 'inventory' && <DateRange value={range} onChange={v => { setRange(v); setPage(1); }} />}<div className="report-filters">
      {(kind === 'sales' || kind === 'customers') && select('customerId', 'Customer', customers.data ?? [])}
      {(kind === 'products' || kind === 'inventory') && select('categoryId', 'Category', categories.data ?? [])}
      {kind === 'products' && select('productId', 'Product', products.data ?? [])}
      {kind === 'sales' && <>{select('status', 'Status', ['Pending', 'Confirmed', 'Completed', 'Cancelled'].map(s => ({ id: s, name: t(s) })))}{['minimumTotal', 'maximumTotal'].map(key => <label key={key}>{t(key === 'minimumTotal' ? 'Minimum total' : 'Maximum total')}<input type="number" min="0" step="0.01" value={filters[key] ?? ''} onChange={e => filter(key, e.target.value)} /></label>)}</>}
      {(kind === 'customers' || kind === 'inventory') && <label>{t('Search by name')}<input type="search" maxLength={150} value={filters.search ?? ''} onChange={e => filter('search', e.target.value)} /></label>}
      {kind === 'inventory' && select('stockStatus', 'Stock status', ['Low', 'Out', 'Healthy'].map(s => ({ id: s, name: t(s) })))}
      {select('sort', 'Sort by', sorts[kind].map(s => ({ id: s, name: t(sortLabels[s]) })))}
      {select('descending', 'Direction', [{ id: 'true', name: t('Descending') }, { id: 'false', name: t('Ascending') }])}
    </div><p className="report-note">{t(kind === 'customers' ? 'Total orders: created in period. Completed orders: completed in period.' : kind === 'products' ? 'Stock is current; sales use stored order prices.' : 'Maximum date range: 366 days. CSV limit: 10,000 rows.')}</p>
    <ErrorNotice error={categories.error ?? customers.error ?? products.error} /></section><ErrorNotice error={exportError} />
    {data.isPending ? <Loading /> : data.isError ? <><ErrorNotice error={data.error} /><button className="button" onClick={() => void data.refetch()}>{t('Try again')}</button></> : data.data && <section className="panel"><Table rows={data.data.items} fields={columns[kind]} /><div className="report-pagination"><span>{t('Total records')}: {number(data.data.totalCount)} · {t('Page')} {number(page)} / {number(Math.max(1, data.data.totalPages))}</span><label>{t('Rows per page')}<select aria-label={t('Rows per page')} value={pageSize} onChange={e => { setPageSize(Number(e.target.value)); setPage(1); }}>{[20, 50, 100].map(n => <option key={n}>{n}</option>)}</select></label><button className="button secondary" disabled={page <= 1} onClick={() => setPage(p => p - 1)}>{t('Previous')}</button><button className="button secondary" disabled={page >= data.data.totalPages} onClick={() => setPage(p => p + 1)}>{t('Next')}</button></div></section>}
  </>;
}
