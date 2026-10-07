import { t, getLanguage } from './i18n';
import { translations } from './translations';
import { Link } from 'react-router-dom';
import { ApiError, errorText } from './api';

export function ErrorNotice({ error }: { error: unknown }) {
  return error ? <div className="notice error" role="alert">{errorText(error)}</div> : null;
}

export function Loading() { return <div className="panel py-12 text-center" role="status">{t("Loading your catalog…")}</div>; }

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
