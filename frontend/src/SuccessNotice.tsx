import { useEffect, useState } from 'react';
import { t } from './i18n';

export function announceSuccess() { window.dispatchEvent(new Event('demoerp:saved')); }
export function SuccessNotice() {
  const [revision, setRevision] = useState(0);
  const [visible, setVisible] = useState(false);
  useEffect(() => {
    const show = () => { setVisible(true); setRevision(v => v + 1); };
    window.addEventListener('demoerp:saved', show);
    return () => window.removeEventListener('demoerp:saved', show);
  }, []);
  useEffect(() => {
    if (!visible) return;
    const timer = window.setTimeout(() => setVisible(false), 5000);
    return () => window.clearTimeout(timer);
  }, [revision, visible]);
  return visible ? <div className="success-feedback" role="status" aria-live="polite"><div className="notice"><span>{t('Changes saved successfully.')}</span><button aria-label={t('Dismiss message')} onClick={() => setVisible(false)}>×</button></div></div> : null;
}
