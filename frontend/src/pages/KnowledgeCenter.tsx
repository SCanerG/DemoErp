import { useEffect, useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api, errorText } from '../api';
import { useAuth } from '../Auth';
import { allowed } from '../permissions';
import { t, useLanguage } from '../i18n';
import { KnowledgeSources, type KnowledgeResult } from '../KnowledgeSources';
type Doc = { id: string; fileName: string; scope: string; version: number; status: string; updatedAt: string; progress: number; error: string | null; chunkCount: number; canManage: boolean; embeddingModel?: string | null; dimensions?: number | null };
const statuses: Record<string, string> = { queued: 'Queued', processing: 'Indexing', indexed: 'Indexed', failed: 'Failed', ocrRequired: 'OCR required', cancelled: 'Cancelled' };
export default function KnowledgeCenter() {
  const language = useLanguage(); const { session } = useAuth(); const cache = useQueryClient();
  const [file, setFile] = useState<File>(); const [scope, setScope] = useState('private'); const [updating, setUpdating] = useState('');
  const [busy, setBusy] = useState(false); const [error, setError] = useState(''); const [message, setMessage] = useState(''); const [filter, setFilter] = useState('');
  const [asking, setAsking] = useState(false); const [turns, setTurns] = useState<{ question: string; result: KnowledgeResult }[]>([]); const [selected, setSelected] = useState('');
  const active = useRef(false); const controller = useRef<AbortController | null>(null);
  useEffect(() => () => controller.current?.abort(), []);
  const status = useQuery({ queryKey: ['knowledge-status'], queryFn: ({ signal }) => api<{ enabled: boolean; vectorAvailable: boolean; embeddingModel: string; maxFileBytes: number }>('/knowledge/status', { signal }), retry: false });
  const library = useQuery({ queryKey: ['knowledge-documents', session?.user.id], enabled: status.data?.enabled === true, queryFn: ({ signal }) => api<Doc[]>('/knowledge/documents', { signal }), refetchInterval: 2000, retry: false });
  const versions = useQuery({ queryKey: ['knowledge-versions', session?.user.id, selected], enabled: !!selected, queryFn: ({ signal }) => api<{ number: number; fileName: string; createdAt: string }[]>(`/knowledge/documents/${selected}/versions`, { signal }), retry: false });
  const canWrite = allowed(session?.user, 'write'); const ready = status.data?.enabled && status.data.vectorAvailable;
  async function refresh() { await cache.invalidateQueries({ queryKey: ['knowledge-documents'] }); await cache.invalidateQueries({ queryKey: ['knowledge-versions'] }); }
  async function upload() {
    if (active.current || !file) return; active.current = true; setBusy(true); setError('');
    try {
      if (file.size > (status.data?.maxFileBytes ?? 5242880)) throw new Error('knowledgeFileSize');
      const data = new FormData(); data.append('file', file); if (!updating) data.append('scope', scope);
      await api(`/knowledge/documents${updating ? '/' + updating : ''}`, { method: updating ? 'PUT' : 'POST', body: data });
      setFile(undefined); setUpdating(''); await refresh();
    } catch (e) { setError(errorText(e)); } finally { active.current = false; setBusy(false); }
  }
  async function change(doc: Doc, action: string) {
    if (active.current) return;
    if (action === 'delete' && !window.confirm(t('Delete this document and all its indexed passages?'))) return;
    active.current = true; setBusy(true); setError('');
    try { await api(`/knowledge/documents/${doc.id}${action === 'delete' ? '' : '/' + action}`, { method: action === 'delete' ? 'DELETE' : 'POST' }); if (selected === doc.id) setSelected(''); setTurns([]); await refresh(); }
    catch (e) { setError(errorText(e)); } finally { active.current = false; setBusy(false); }
  }
  async function ask() {
    if (asking || !message.trim() || !ready) return; setAsking(true); setError('');
    const request = new AbortController(); controller.current = request;
    try { const result = await api<KnowledgeResult>('/knowledge/chat', { method: 'POST', body: JSON.stringify({ message: message.trim(), language, ...(filter ? { documentId: filter } : {}) }), signal: request.signal }); setTurns(previous => [...previous.slice(-9), { question: message.trim(), result }]); }
    catch (e) { setError(request.signal.aborted ? t('Request cancelled.') : errorText(e)); } finally { controller.current = null; setAsking(false); }
  }
  return <section className="space-y-6">
    <div className="flex flex-wrap justify-between gap-4"><div><p className="eyebrow">{t('BUSINESS INSIGHTS')}</p><h1 className="text-3xl font-semibold">{t('Knowledge Center')}</h1><p className="mt-3 max-w-2xl text-sm leading-6 text-slate-500">{t('Search authorized manuals and policies. Operational ERP facts and documented procedures are shown separately.')}</p></div><Link to="/agent-center" className="button-secondary">{t('Agent Control Center')}</Link></div>
    {status.isPending && <p role="status">{t('Checking availability…')}</p>}{status.error && <p role="alert" className="error-box">{errorText(status.error)}</p>}
    {status.data && <p className="notice">{t(!status.data.enabled ? 'knowledgeDisabled' : !status.data.vectorAvailable ? 'knowledgeVectorUnavailable' : 'Local knowledge retrieval ready')} · {status.data.embeddingModel}</p>}
    {error && <p role="alert" className="error-box">{error}</p>}
    {canWrite && status.data?.enabled && <form className="panel space-y-4" onSubmit={e => { e.preventDefault(); void upload(); }}>
      <h2 className="text-lg font-semibold">{t(updating ? 'Upload a new document version' : 'Upload document')}</h2>
      <label className="field">{t('Document file')}<input key={updating} type="file" accept=".pdf,.docx,.txt,.md" disabled={busy} onChange={e => setFile(e.target.files?.[0])} /></label>
      {!updating && <label className="field">{t('Document access')}<select value={scope} disabled={busy} onChange={e => setScope(e.target.value)}><option value="private">{t('Private to my account')}</option>{session?.user.role === 'Admin' && <option value="workspace">{t('Shared business workspace')}</option>}</select></label>}
      <p className="text-xs text-slate-500">PDF · DOCX · TXT · Markdown · {Math.round((status.data?.maxFileBytes ?? 5242880)/1048576)} MB · {t('Scanned PDFs require OCR. Uploaded instructions cannot authorize ERP actions.')}</p>
      <div className="flex gap-3"><button className="button" disabled={busy || !file}>{t(busy ? 'Saving…' : 'Upload document')}</button>{updating && <button type="button" className="button-secondary" onClick={() => { setUpdating(''); setFile(undefined); }}>{t('Cancel')}</button>}</div>
    </form>}
    <section className="panel space-y-4"><h2 className="text-lg font-semibold">{t('Document library')}</h2>
      {library.error && <p role="alert" className="error-box">{errorText(library.error)}</p>}{library.isFetching && !library.data && <p role="status">{t('Loading…')}</p>}
      {library.data?.length === 0 && <p className="text-sm text-slate-500">{t('No accessible documents. Upload a manual or ask an administrator to share one.')}</p>}
      <ul className="space-y-3">{library.data?.map(doc => <li key={doc.id} className="rounded-lg border border-slate-200 p-4"><div className="flex flex-wrap justify-between gap-3"><button type="button" className="text-link break-all text-left" onClick={() => setSelected(doc.id)}>{doc.fileName}</button><span className="ai-badge">{t(statuses[doc.status] ?? 'Failed')} · {doc.progress}%</span></div><p className="mt-2 text-xs text-slate-500">{t(doc.scope === 'private' ? 'Private to my account' : 'Shared business workspace')} · {t('Version')} {doc.version} · {doc.chunkCount} {t('Passages')}{doc.embeddingModel && <> · {doc.embeddingModel} · {doc.dimensions}</>}</p>{doc.error && <p className="mt-2 text-sm text-amber-800">{t(doc.error)}</p>}{doc.canManage && <div className="mt-3 flex flex-wrap gap-3">{[['reindex', 'Reindex'], ['cancel', 'Cancel indexing'], ['delete', 'Delete']].map(([action, label]) => <button type="button" key={action} className="text-link text-sm" disabled={busy} onClick={() => void change(doc, action)}>{t(label)}</button>)}<button className="text-link text-sm" disabled={busy} onClick={() => { setUpdating(doc.id); setFile(undefined); }}>{t('Upload a new document version')}</button></div>}</li>)}</ul>
      {selected && <div className="rounded-lg bg-slate-50 p-4"><h3 className="font-medium">{t('Document versions')}</h3>{versions.error && <p role="alert">{errorText(versions.error)}</p>}<ul className="mt-2 space-y-2 text-sm">{versions.data?.map(v => <li key={v.number}>{t('Version')} {v.number} · {v.fileName} · {new Date(v.createdAt).toLocaleString(language === 'tr' ? 'tr-TR' : 'en-US')}</li>)}</ul></div>}
    </section>
    <section className="panel space-y-5"><h2 className="text-lg font-semibold">{t('Knowledge Agent')}</h2><form className="space-y-4" onSubmit={e => { e.preventDefault(); void ask(); }}>
      <label className="field">{t('Knowledge sources')}<select aria-label={t('Knowledge sources')} value={filter} disabled={asking} onChange={e => setFilter(e.target.value)}><option value="">{t('All accessible documents')}</option>{library.data?.filter(d => d.status === 'indexed').map(d => <option key={d.id} value={d.id}>{d.fileName}</option>)}</select></label>
      <label className="field">{t('Documentation question')}<textarea rows={3} maxLength={2000} value={message} disabled={!ready || asking} onChange={e => setMessage(e.target.value)} /></label>
      <div className="flex gap-3"><button className="button" disabled={!ready || asking || !message.trim()}>{t(asking ? 'Searching authorized documents…' : 'Ask knowledge agent')}</button>{asking && <button type="button" className="button-secondary" onClick={() => controller.current?.abort()}>{t('Cancel')}</button>}</div>
    </form><p className="text-xs text-slate-500">{t('Each question searches current authorized sources independently. Conversation stays in this browser tab.')}</p>{turns.length > 0 && <button type="button" className="text-link text-sm" onClick={() => setTurns([])}>{t('Clear conversation')}</button>}{turns.map((turn, i) => <article className="space-y-3 border-t border-slate-100 pt-4" key={i}><p className="whitespace-pre-wrap break-words font-medium">{turn.question}</p><KnowledgeSources result={turn.result} /></article>)}</section>
  </section>;
}
