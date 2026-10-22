import { t, useLanguage } from './i18n';
export type OrderDraft = {
  customerId: string | null; customer: string | null; warehouseId: string | null; warehouse: string | null;
  lines: { productId: string; product: string; quantity: number; unitPrice: number; lineTotal: number; availableStock: number; shortage: number }[];
  total: number | null; currency: string; notSubmitted: boolean; persisted: boolean; requiresHumanReview: boolean;
  clarifications: { field: string; query: string; reason: string; candidates: { id: string; name: string }[] }[];
};
export function OrderDraftPreview({ draft }: { draft: OrderDraft }) {
  const language = useLanguage();
  const money = (value: number) => new Intl.NumberFormat(language === 'tr' ? 'tr-TR' : 'en-US', { style: 'currency', currency: 'USD' }).format(value);
  return <section className="panel space-y-4"><h2 className="text-lg font-semibold">{t('Order draft preview')}</h2><span className="ai-badge">{t('Not submitted')}</span>
    <p>{t('Customer')}: {draft.customer ?? '—'} · {t('Warehouse')}: {draft.warehouse ?? '—'}</p>
    <div className="rounded-lg bg-amber-50 p-4" role="status"><h3 className="font-medium">{t('Clarification required')}</h3><ul className="mt-2 list-disc space-y-2 pl-5">{draft.clarifications.map((issue, i) => <li key={i}>{t(({ customer: 'Customer', product: 'Product', quantity: 'Quantity' } as Record<string, string>)[issue.field] ?? issue.field)}: {issue.query} — {t(({ missing: 'Missing information', notFound: 'Record not found', ambiguous: 'Multiple matches', duplicate: 'Duplicate product' } as Record<string, string>)[issue.reason] ?? 'Check the submitted fields.')}{issue.candidates.length > 0 && <p className="mt-1 text-sm">{issue.candidates.map(c => c.name).join(' · ')}</p>}</li>)}</ul></div>
    {draft.lines.map(line => <p key={line.productId}>{line.product} · {line.quantity} · {money(line.lineTotal)}</p>)}
    <p className="text-sm text-amber-800">{t('Resolve the missing or ambiguous draft fields before saving an order.')}</p>
  </section>;
}
