import { t } from './i18n';

// The steps describe the workflow, not a fabricated event history.
export function StatusProgress({ status, steps }: { status: string; steps: string[] }) {
  return <div className="status-progress" aria-label={t('Status workflow')}>
    {status === 'Cancelled' ? <p className="status-Cancelled">{t(status)}</p> : <ol>{steps.map((step, index) => <li key={step} aria-current={status === step ? 'step' : undefined}><span aria-hidden="true">{index + 1}</span>{t(step)}</li>)}</ol>}
  </div>;
}
