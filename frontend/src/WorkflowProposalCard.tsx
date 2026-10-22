import { useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api, errorText } from './api';
import { useAuth } from './Auth';
import { allowed } from './permissions';
import { t, useLanguage } from './i18n';

export type WorkflowProposal = {
  id: string; executionId: string; kind: 'sales' | 'purchase'; state: string; fingerprint: string; createdAt: string; expiresAt: string;
  facts: { counterpartyId: string; counterparty: string; warehouseId: string | null; warehouse: string | null; currency: string;
    lines: { productId: string; product: string; quantity: number; unitPrice: number; stock: number; priceSourceId: string | null }[] };
  resultId: string | null; resultNumber: string | null; resultStatus: string | null; errorCategory: string | null; completedAt?: string | null; resultVerified: boolean;
};
export function WorkflowProposalCard({ proposal, onSaving, blocked }: { proposal: WorkflowProposal; onSaving: (saving: boolean) => void; blocked: boolean }) {
  const language = useLanguage(); const { session } = useAuth(); const cache = useQueryClient();
  const [updated, setUpdated] = useState<WorkflowProposal>(); const value = updated ?? proposal;
  const [reviewed, setReviewed] = useState(false); const [saving, setSaving] = useState(false); const [failure, setFailure] = useState('');
  const key = useRef(crypto.randomUUID()); const active = useRef(false);
  const canWrite = allowed(session?.user, 'write'); const purchase = value.kind === 'purchase';
  const expired = new Date(value.expiresAt).getTime() <= Date.now(); const pending = value.state === 'pending' && !expired;
  const money = (number: number) => new Intl.NumberFormat(language === 'tr' ? 'tr-TR' : 'en-US', { style: 'currency', currency: 'USD' }).format(number);
  const total = value.facts.lines.reduce((sum, line) => sum + line.quantity * line.unitPrice, 0);
  async function action(approve: boolean) {
    if (active.current || blocked || !pending || (approve && (!canWrite || !reviewed))) return;
    active.current = true; setSaving(true); onSaving(true); setFailure('');
    try {
      const next = await api<WorkflowProposal>(`/ai/agents/proposals/${value.id}/${approve ? 'approve' : 'cancel'}`, { method: 'POST', ...(approve ? { body: JSON.stringify({ idempotencyKey: key.current, fingerprint: value.fingerprint }) } : {}) });
      setUpdated(next);
      void Promise.all(['agent-proposals', 'orders', 'purchase-orders', 'customers', 'reports', 'dashboard', 'audit-logs'].map(queryKey => cache.invalidateQueries({ queryKey: [queryKey] })));
    } catch (error) { setFailure(errorText(error)); void cache.invalidateQueries({ queryKey: ['agent-proposals'] }); }
    finally { active.current = false; setSaving(false); onSaving(false); }
  }
  return <section className="panel space-y-4">
    <div className="flex flex-wrap justify-between gap-2"><h2 className="text-lg font-semibold">{t(purchase ? 'Purchase order proposal' : 'Order draft preview')}</h2><span className="ai-badge">{t(value.state === 'approved' ? value.resultVerified ? purchase ? 'Purchase draft saved' : 'Saved as pending' : 'Result requires review' : expired && value.state === 'pending' ? 'Proposal expired' : value.state === 'pending' ? 'Pending approval' : value.state === 'cancelled' ? 'Cancelled' : 'Proposal requires regeneration')}</span></div>
    <p>{t(purchase ? 'Supplier' : 'Customer')}: {value.facts.counterparty}{value.facts.warehouse && <> · {t('Warehouse')}: {value.facts.warehouse}</>}</p>
    <p className="text-xs text-slate-500">{t('Proposal expires at')}: {new Date(value.expiresAt).toLocaleString(language === 'tr' ? 'tr-TR' : 'en-US')}</p>
    {value.completedAt && <p className="text-xs text-slate-500">{t('Ended at')}: {new Date(value.completedAt).toLocaleString(language === 'tr' ? 'tr-TR' : 'en-US')}</p>}
    {purchase && <p className="text-sm text-slate-500">{t('Purchase costs use the latest submitted purchase history, not a current supplier quote. Saving creates a purchase draft; submission and receiving remain separate actions.')}</p>}
    <div className="overflow-x-auto" role="region" tabIndex={0} aria-label={t('Proposal items')}><table className="w-full text-left text-sm"><caption className="sr-only">{t('Proposal items')}</caption><thead><tr>{['Product', 'Quantity', 'Unit price', 'Line total', 'Available stock'].map(label => <th key={label} className="p-2">{t(label)}</th>)}</tr></thead><tbody>{value.facts.lines.map(line => <tr key={line.productId} className="border-t border-slate-100"><td className="p-2">{line.product}</td><td className="p-2">{line.quantity}</td><td className="p-2">{money(line.unitPrice)}</td><td className="p-2">{money(line.unitPrice * line.quantity)}</td><td className="p-2">{line.stock}</td></tr>)}</tbody></table></div>
    <p className="font-semibold">{t('Total')}: {money(total)}</p>
    {value.state === 'approved' && value.resultId && value.resultVerified ? <div className="rounded-lg bg-emerald-50 p-4" role="status"><p>{t('Order saved')}: {value.resultNumber} · {t(value.resultStatus ?? '')} · {money(total)}</p><Link className="mt-2 inline-block text-link" to={`${purchase ? '/purchase-orders' : '/orders'}/${value.resultId}`}>{t('View order')}</Link></div> : pending ? canWrite ? <div className="space-y-3 border-t border-slate-100 pt-4">
      <p className="text-sm text-slate-500">{t('Approval rechecks the exact proposal, permissions, prices and stock. Changed data requires a new proposal. No stock is reserved.')}</p>
      <label className="flex items-start gap-2 text-sm"><input className="mt-1" type="checkbox" checked={reviewed} disabled={saving || blocked} onChange={e => setReviewed(e.target.checked)} />{t(purchase ? 'I reviewed the supplier, quantities and purchase costs and approve saving this draft.' : 'I reviewed the customer, warehouse and items and approve saving this order.')}</label>
      <div className="flex flex-wrap gap-3"><button type="button" className="button" disabled={!reviewed || saving || blocked} onClick={() => void action(true)}>{t(saving ? 'Saving…' : 'Save order')}</button><button type="button" className="button-secondary" disabled={saving || blocked} onClick={() => void action(false)}>{t('Cancel proposal')}</button></div>
    </div> : <p className="text-sm text-slate-500">{t('Only administrators and operations managers can save an order.')}</p> : null}
    {value.errorCategory && <p role="alert" className="error-box">{t(value.errorCategory)}</p>}
    {failure && <div role="alert" className="error-box"><p>{failure}</p><p className="mt-2">{t('Reload the execution to check its verified result. Retrying this approval uses the same operation key.')}</p></div>}
  </section>;
}
