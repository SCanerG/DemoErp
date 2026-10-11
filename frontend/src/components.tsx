import { t, getLanguage } from './i18n';
import { translations } from './translations';
import { Link } from 'react-router-dom';
import { ApiError, errorText } from './api';

export function ErrorNotice({ error }: { error: unknown }) {
  return error ? <div className="notice error" role="alert">{errorText(error)}</div> : null;
}

export function Loading() { return <div className="panel loading-skeleton" role="status" aria-busy="true"><span className="sr-only">{t("Loading workspace…")}</span><div className="skeleton" /><div className="skeleton" /><div className="skeleton" /></div>; }

export function QueryError({ error, retry, back = '/products' }: { error: unknown; retry: () => void; back?: string }) {
  const missing = error instanceof ApiError && error.status === 404;
  return <section className="panel py-12 text-center">
    <h1 className="text-2xl font-semibold">{back === '/products' ? missing ? t("Product not found") : t("Could not load products") : missing ? t('Record not found') : t('Could not load records')}</h1>
    <p className="my-4 text-slate-600">{errorText(error)}</p>
    {missing ? <Link className="button" to={back}>{t(back === '/products' ? 'Back to products' : 'Back to list')}</Link>
      : <button className="button" onClick={retry}>{t("Try again")}</button>}
  </section>;
}

export function FieldError({ message, id }: { message?: string; id?: string }) {
  return message ? <span id={id} className="field-error" role="alert">{getLanguage() === 'tr' && !translations[message] && !Object.values(translations).includes(message) ? t('Check the submitted fields.') : t(message)}</span> : null;
}

export function StatusBadge({ active }: { active: boolean }) {
  return <span className={`badge ${active ? 'active' : 'inactive'}`}><span aria-hidden="true">●</span> {active ? t("Active") : t("Inactive")}</span>;
}

export const priceFormat = { format: (value: number) => new Intl.NumberFormat(getLanguage() === 'tr' ? 'tr-TR' : 'en-US', { style: 'currency', currency: 'USD' }).format(value) };
export function dateFormat(value: string) { return new Date(value).toLocaleDateString(getLanguage() === 'tr' ? 'tr-TR' : 'en-US', { month: 'short', day: 'numeric', year: 'numeric' }); }

export function BusinessStatus({ status }: { status: string }) {
  return <span className={`badge status-${status}`}>{t(status)}</span>;
}
export function DashboardSkeleton() {
  return <div role="status" aria-busy="true"><span className="sr-only">{t('Loading workspace…')}</span><div className="kpi-grid">{Array.from({ length: 8 }, (_, i) => <div className="kpi-card loading-skeleton" key={i} aria-hidden="true"><div className="skeleton" /><div className="skeleton" /><div className="skeleton" /></div>)}</div><div className="dashboard-charts" aria-hidden="true"><div className="panel chart-box skeleton" /><div className="panel chart-box skeleton" /></div></div>;
}

export function dateTimeFormat(value: string) { return new Date(value).toLocaleString(getLanguage() === 'tr' ? 'tr-TR' : 'en-US'); }
