import { Can } from '../permissions';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useIsMutating, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { api } from '../api';
import { t } from '../i18n';
import { dateFormat, ErrorNotice, FieldError, Loading, QueryError } from '../components';
import { DeleteConfirmation } from '../DeleteConfirmation';
import type { Inventory, InventoryMovement } from '../types';

export function StockBadge({ inventory }: { inventory: Pick<Inventory, 'quantityOnHand' | 'minimumStockLevel'> }) {
  const status = inventory.quantityOnHand === 0 ? 'out' : inventory.quantityOnHand <= inventory.minimumStockLevel ? 'low' : 'in';
  return <span className={`badge stock-${status}`}>{t(status === 'out' ? 'Out of stock' : status === 'low' ? 'Low stock' : 'In stock')}</span>;
}

export function InventoryList() {
  const query = useQuery({ queryKey: ['inventory'], queryFn: ({ signal }) => api<Inventory[]>('/inventory', { signal }) });
  if (query.isPending) return <Loading />;
  if (query.isError) return <QueryError error={query.error} retry={() => void query.refetch()} back="/inventory" />;
  const rows = query.data;
  return <><div className="page-heading"><div><p className="eyebrow">{t('YOUR WORKSPACE')}</p><h1>{t('Inventory')}</h1><p>{t('Track stock and its complete movement history.')}</p></div></div>
    <div className="stats-grid order-stats">{[
      ['Total products', rows.length], ['Products in stock', rows.filter(i => i.quantityOnHand > 0).length],
      ['Out of stock products', rows.filter(i => i.quantityOnHand === 0).length],
      ['Low stock products', rows.filter(i => i.quantityOnHand > 0 && i.quantityOnHand <= i.minimumStockLevel).length],
    ].map(([label, count]) => <div className="stat" key={label}><span>{t(String(label))}</span><strong>{count}</strong></div>)}</div>
    <section className="panel overflow-hidden p-0">{!rows.length ? <div className="p-10 text-center"><h2>{t('No records yet')}</h2><p className="mt-3 text-sm text-slate-500">{t('Create a product to initialize inventory.')}</p><Can permission="write"><Link className="button mt-5" to="/products/new">{t('New product')}</Link></Can></div> : <>
      <div className="hidden overflow-x-auto md:block"><table><thead><tr>{['Product name', 'Category', 'Quantity on hand', 'Minimum stock level', 'Stock status', 'Last updated', 'Actions'].map(label => <th key={label}>{t(label)}</th>)}</tr></thead><tbody>{rows.map(i => <tr key={i.id}><td><Link className="text-link" to={`/products/${i.productId}`}>{i.productName}</Link>{!i.isActive && <p className="mt-1 text-xs text-slate-500">{t('Inactive')}</p>}</td><td>{i.categoryName}</td><td className="tabular-nums font-semibold">{i.quantityOnHand}</td><td>{i.minimumStockLevel}</td><td><StockBadge inventory={i} /></td><td>{dateFormat(i.updatedAt)}</td><td><Link className="text-link" to={`/inventory/${i.productId}`}>{t('View inventory')}</Link></td></tr>)}</tbody></table></div>
      <ul className="divide-y divide-slate-100 md:hidden">{rows.map(i => <li className="space-y-3 p-5" key={i.id}><div className="flex justify-between gap-2"><Link className="text-link break-words" to={`/inventory/${i.productId}`}>{i.productName}</Link><StockBadge inventory={i} /></div><p className="text-sm text-slate-500">{i.categoryName} · {t(i.isActive ? 'Active' : 'Inactive')}</p><p>{t('Quantity on hand')}: <strong>{i.quantityOnHand}</strong></p><p className="text-sm">{t('Minimum stock level')}: {i.minimumStockLevel}</p><p className="text-xs text-slate-500">{t('Last updated')}: {dateFormat(i.updatedAt)}</p><Link className="text-link inline-block" to={`/inventory/${i.productId}`}>{t('View inventory')}</Link></li>)}</ul>
    </>}</section></>;
}

type Operation = 'stock-in' | 'stock-out' | 'adjust' | 'minimum-level';
const labels: Record<Operation, string> = { 'stock-in': 'Stock in', 'stock-out': 'Stock out', adjust: 'Adjust stock', 'minimum-level': 'Minimum stock level' };
const movementLabels: Record<string, string> = { StockIn: 'Stock in', StockOut: 'Stock out', AdjustmentIncrease: 'Adjustment increase', AdjustmentDecrease: 'Adjustment decrease', OrderDeduction: 'Order deduction', OrderCancellationReturn: 'Order cancellation return' };
const reasonText = (movement: InventoryMovement) => movement.referenceType === 'Order' ? t(movement.reason) : movement.reason;

export function InventoryDetail() {
  const { productId } = useParams();
  const [operation, setOperation] = useState<Operation>('stock-in');
  const busy = useIsMutating({ mutationKey: ['inventory', productId] }) > 0;
  const query = useQuery({ queryKey: ['inventory', productId], queryFn: ({ signal }) => api<Inventory>(`/inventory/${productId}`, { signal }) });
  const movements = useQuery({ queryKey: ['inventory', productId, 'movements'], queryFn: ({ signal }) => api<InventoryMovement[]>(`/inventory/${productId}/movements`, { signal }) });
  if (query.isPending) return <Loading />;
  if (query.isError) return <QueryError error={query.error} retry={() => void query.refetch()} back="/inventory" />;
  const value = query.data;
  return <><Link className="back-link" to="/inventory">← {t('Back to inventory')}</Link><div className="page-heading"><div><p className="eyebrow">{t('Inventory details')}</p><h1 className="break-words">{value.productName}</h1><p>{value.categoryName} · {t(value.isActive ? 'Active' : 'Inactive')}</p></div><Link className="button secondary" to={`/products/${productId}`}>{t('Product information')}</Link></div>
    <section className="panel mb-6"><div className="flex flex-wrap justify-between gap-4"><div><span className="text-sm text-slate-500">{t('Quantity on hand')}</span><p className="mt-2 text-4xl font-semibold" data-testid="stock-quantity">{value.quantityOnHand}</p></div><div><span className="text-sm text-slate-500">{t('Minimum stock level')}</span><p className="mt-2 text-2xl" data-testid="minimum-level">{value.minimumStockLevel}</p></div><StockBadge inventory={value} /></div><p className="mt-5 text-xs text-slate-500">{t('Last updated')}: {new Intl.DateTimeFormat(document.documentElement.lang, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value.updatedAt))}</p></section>
    <Can permission="write"><section className="panel mb-6"><div className="mb-6 flex flex-wrap gap-2" role="group" aria-label={t('Inventory operations')}>{(Object.keys(labels) as Operation[]).map(key => <button className={`button ${operation === key ? '' : 'secondary'}`} key={key} aria-pressed={operation === key} disabled={busy} onClick={() => setOperation(key)}>{t(labels[key])}</button>)}</div><StockForm key={operation} inventory={value} operation={operation} /></section></Can>
    <section className="panel overflow-hidden"><h2 className="mb-5 text-xl font-semibold">{t('Movement history')}</h2>{movements.isPending ? <Loading /> : movements.isError ? <QueryError error={movements.error} retry={() => void movements.refetch()} back="/inventory" /> : !movements.data.length ? <p className="text-sm text-slate-500">{t('No inventory movements yet.')}</p> : <>
      <div className="hidden overflow-x-auto md:block"><table><thead><tr>{['Date', 'Movement type', 'Quantity', 'Before', 'After', 'Reason', 'Reference', 'Performed by'].map(label => <th key={label}>{t(label)}</th>)}</tr></thead><tbody>{movements.data.map(m => <tr key={m.id}><td className="whitespace-nowrap">{new Intl.DateTimeFormat(document.documentElement.lang, { dateStyle: 'short', timeStyle: 'short' }).format(new Date(m.createdAt))}</td><td>{t(movementLabels[m.movementType])}</td><td>{m.quantity}</td><td>{m.quantityBefore}</td><td>{m.quantityAfter}</td><td className="min-w-40 break-words">{reasonText(m)}</td><td>{m.referenceId ? <Link className="text-link" to={`/orders/${m.referenceId}`}>{m.orderNumber}</Link> : t('Manual')}</td><td>{m.performedBy}</td></tr>)}</tbody></table></div>
      <ul className="divide-y divide-slate-100 md:hidden">{movements.data.map(m => <li className="space-y-3 py-5" key={m.id}><div className="flex flex-wrap justify-between gap-2"><strong>{t(movementLabels[m.movementType])}</strong><span>{t('Quantity')}: {m.quantity}</span></div><p>{t('Before')}: {m.quantityBefore} → {t('After')}: {m.quantityAfter}</p><p className="break-words text-sm">{reasonText(m)}</p><p className="text-xs text-slate-500">{dateFormat(m.createdAt)} · {t('Performed by')}: {m.performedBy}</p>{m.referenceId ? <Link className="text-link break-all" to={`/orders/${m.referenceId}`}>{m.orderNumber}</Link> : <p className="text-sm">{t('Manual')}</p>}</li>)}</ul>
    </>}</section></>;
}

function StockForm({ inventory, operation }: { inventory: Inventory; operation: Operation }) {
  const minimum = operation === 'minimum-level';
  const schema = z.object({
    quantity: z.number({ error: 'Enter a valid whole number.' }).int('Enter a valid whole number.').min(minimum ? 0 : 1, minimum ? 'Minimum stock level cannot be negative.' : 'Quantity must be a whole number greater than zero.').max(2147483647, 'Stock limit exceeded.'),
    reason: minimum ? z.string() : z.string().trim().min(1, 'Enter a reason.').max(1000, 'Maximum length exceeded.'),
    direction: z.enum(['Increase', 'Decrease']),
  });
  type Input = z.infer<typeof schema>;
  const { register, handleSubmit, reset, formState: { errors } } = useForm<Input>({ resolver: zodResolver(schema), defaultValues: { quantity: minimum ? inventory.minimumStockLevel : 1, reason: '', direction: 'Increase' } });
  const cache = useQueryClient();
  const [confirmation, setConfirmation] = useState<Input | null>(null);
  const save = useMutation({ mutationKey: ['inventory', inventory.productId], mutationFn: (input: Input) => api<Inventory>(`/inventory/${inventory.productId}/${operation}`, { method: minimum ? 'PUT' : 'POST', body: JSON.stringify(minimum ? { minimumStockLevel: input.quantity } : operation === 'adjust' ? input : { quantity: input.quantity, reason: input.reason }) }), onSuccess: async result => {
    cache.setQueryData(['inventory', inventory.productId], result);
    await cache.invalidateQueries({ queryKey: ['inventory'] });
    setConfirmation(null); reset({ quantity: minimum ? result.minimumStockLevel : 1, reason: '', direction: 'Increase' });
  } });
  function submit(input: Input) {
    if (operation === 'stock-out' || operation === 'adjust' && input.direction === 'Decrease') setConfirmation(input);
    else save.mutate(input);
  }
  return <><form noValidate onSubmit={handleSubmit(submit)}><fieldset className="max-w-xl space-y-5" disabled={save.isPending}><h2 className="text-xl font-semibold">{t(labels[operation])}</h2><ErrorNotice error={confirmation ? undefined : save.error} />{save.isSuccess && <p className="notice" role="status">{t('Inventory updated successfully.')}</p>}
    {operation === 'adjust' && <label className="field">{t('Direction')}<select aria-label={t('Direction')} {...register('direction')}><option value="Increase">{t('Increase')}</option><option value="Decrease">{t('Decrease')}</option></select></label>}
    <label className="field">{t(minimum ? 'Minimum stock level' : 'Quantity')} *<input type="number" aria-label={t(minimum ? 'Minimum stock level' : 'Quantity')} min={minimum ? 0 : 1} max={2147483647} step={1} {...register('quantity', { valueAsNumber: true })} aria-invalid={!!errors.quantity} /><FieldError message={errors.quantity?.message} /></label>
    {!minimum && <label className="field">{t('Reason')} *<textarea aria-label={t('Reason')} rows={3} maxLength={1000} {...register('reason')} aria-invalid={!!errors.reason} /><FieldError message={errors.reason?.message} /></label>}
    <button className="button" disabled={save.isPending}>{t(save.isPending ? 'Saving…' : 'Apply operation')}</button>
  </fieldset></form>{confirmation && <DeleteConfirmation product={{ name: inventory.productName }} title="Confirm stock decrease?" description={`${t("This operation will reduce available stock and create a permanent audit entry.")} ${inventory.productName} · ${t("Quantity")}: ${confirmation.quantity}`} confirmLabel="Confirm decrease" pending={save.isPending} error={save.error} onCancel={() => setConfirmation(null)} onDelete={() => save.mutate(confirmation)} />}</>;
}
