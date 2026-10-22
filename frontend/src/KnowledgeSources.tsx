import { useState } from 'react';
import { api, errorText } from './api';
import { t } from './i18n';
export type KnowledgeResult = { answer: string; language: string; responseKind: string; insufficientEvidence: boolean; retrievalMs: number; llmMs: number; totalTokens: number | null; embeddingModel: string; dimensions: number;
  citations: { number: number; documentId: string; version: number; chunkId: string; fileName: string; reference: string; quote: string }[] };
export function KnowledgeSources({ result }: { result: KnowledgeResult }) {
  const [preview, setPreview] = useState(''); const [failure, setFailure] = useState('');
  async function show(c: KnowledgeResult['citations'][number]) {
    setPreview(''); setFailure('');
    try { const source = await api<{ text: string }>(`/knowledge/documents/${c.documentId}/chunks/${c.chunkId}`); setPreview(source.text); }
    catch (error) { setFailure(errorText(error)); }
  }
  return <section className="space-y-4" aria-label={t('Knowledge sources')}>
    <h3 className="font-semibold">{t(result.responseKind === 'model_extract' ? 'Model-selected verified source excerpts' : result.responseKind === 'mock' ? 'Development mock' : 'Insufficient verified evidence')}</h3>
    <p className="whitespace-pre-wrap break-words text-sm leading-7">{result.answer}</p>
    <ul className="space-y-3">{result.citations.map(c => <li key={c.number} className="rounded-lg border border-slate-200 p-4"><p className="text-sm font-medium">[{c.number}] {c.fileName} · {t('Version')} {c.version} · {c.reference}</p><blockquote className="mt-2 whitespace-pre-wrap break-words text-sm leading-6 text-slate-600">{c.quote}</blockquote><button type="button" className="mt-2 text-link text-sm" onClick={() => void show(c)}>{t('Read source passage')}</button></li>)}</ul>
    {failure && <p role="alert" className="error-box">{failure}</p>}{preview && <div className="rounded-lg bg-slate-50 p-4"><h4 className="font-medium">{t('Source passage')}</h4><p className="mt-2 whitespace-pre-wrap break-words text-sm leading-6">{preview}</p></div>}
    <p className="text-xs text-slate-500">{result.embeddingModel} · {result.dimensions} · {t('Retrieval')}: {result.retrievalMs} ms · {t('Model')}: {result.llmMs} ms · {t('Tokens')}: {result.totalTokens ?? '—'}</p>
  </section>;
}
