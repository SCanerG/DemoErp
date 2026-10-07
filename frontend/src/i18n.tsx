import { useSyncExternalStore } from 'react';
import { translations, businessEnglish } from './translations';
export type Language = 'en' | 'tr';
function readLanguage(): Language { try { return localStorage.getItem('catalog.language') === 'tr' ? 'tr' : 'en'; } catch { return 'en'; } }
let language = readLanguage();
const listeners = new Set<() => void>();
document.documentElement.lang = language;
export function getLanguage() { return language; }
export function setLanguage(value: Language) {
  language = value; document.documentElement.lang = value;
  try { localStorage.setItem('catalog.language', value); } catch { /* In-memory preference still works. */ }
  listeners.forEach(listener => listener());
}
export function useLanguage() { return useSyncExternalStore(callback => { listeners.add(callback); return () => { listeners.delete(callback); }; }, getLanguage); }
const reverse = new Map(Object.entries(translations).map(([en, tr]) => [tr, en]));
export function t(text: string): string {
  const key = reverse.get(text) ?? text;
  return language === 'tr' ? translations[key] ?? key : businessEnglish[key] ?? key;
}
export function LanguageSelector() {
  const value = useLanguage();
  return <div className="language-selector" aria-label={t('Language')}>
    {(['tr', 'en'] as const).map(lang => <button key={lang} type="button" aria-pressed={value === lang} onClick={() => setLanguage(lang)}>{lang.toUpperCase()}</button>)}
  </div>;
}
