import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api, errorText } from '../api';
import { t, useLanguage } from '../i18n';
import { useAuth } from '../Auth';
import { AiResponseText } from '../AiResponseText';

type Source = { type: string; name: string; label: string; parameters: { startDate?: string; endDate?: string }; reportPath: string };
type Answer = { answer: string; language: string; generatedAtUtc: string; sources: Source[]; requestId: string; conversationId?: string; isMock?: boolean; responseKind?: string };
type Turn = { question: string; response?: Answer; error?: string };
const questions = ['Summarize sales for the last 30 days.', 'Show the top five selling products.', 'Which products have low stock?', 'How many orders are pending?', 'Who are our top customers?'];
const reportPaths = new Set(['/reports/sales', '/reports/products', '/reports/customers', '/reports/inventory', '/orders']);

export default function AiAssistant() {
  const language = useLanguage();
  const { session } = useAuth();
  const [remember, setRemember] = useState(false);
  const [agent, setAgent] = useState('reporting');
  const [conversationId, setConversationId] = useState<string>();
  const [historyBusy, setHistoryBusy] = useState(false);
  const [historyError, setHistoryError] = useState('');
  const saved = useQuery({ queryKey: ['ai-conversations', session?.user.id], enabled: remember,
    queryFn: ({ signal }) => api<{ id: string; createdAt: string; expiresAt: string; turns: number }[]>('/ai/conversations', { signal }), retry: false });
  const [message, setMessage] = useState('');
  const [turns, setTurns] = useState<Turn[]>([]);
  const controller = useRef<AbortController | null>(null);
  const active = useRef(false);
  const mounted = useRef(true);
  const status = useQuery({ queryKey: ['ai-status'], queryFn: ({ signal }) => api<{ enabled: boolean; configured: boolean; available: boolean; provider?: string; isMock?: boolean; retentionDays?: number }>('/ai/status', { signal }), retry: false });
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; controller.current?.abort(); }; }, []);
  const mutation = useMutation({ meta: { affectsBusinessData: false }, retry: false,
    mutationFn: ({ question, signal, selectedLanguage }: { question: string; signal: AbortSignal; selectedLanguage: string }) =>
      api<Answer>('/ai/chat', { method: 'POST', body: JSON.stringify({ message: question, language: selectedLanguage, ...(agent === 'inventory' ? { agent } : {}), ...(remember ? { remember: true, ...(conversationId ? { conversationId } : {}) } : {}) }), signal }),
  });
  async function submit() {
    const question = message.trim();
    if (!question || question.length > 2000 || active.current || historyBusy || !status.data?.available) return;
    active.current = true;
    const request = new AbortController(); controller.current = request;
    setTurns(previous => [...previous.slice(-19), { question }]); setMessage('');
    try {
      const response = await mutation.mutateAsync({ question, signal: request.signal, selectedLanguage: language });
      if (mounted.current && !request.signal.aborted) {
        setTurns(previous => previous.map((turn, i) => i === previous.length - 1 ? { ...turn, response } : turn));
        if (response.conversationId) { setConversationId(response.conversationId); void saved.refetch(); }
      }
    } catch (error) {
      if (mounted.current) setTurns(previous => previous.map((turn, i) => i === previous.length - 1 ? { ...turn, error: request.signal.aborted ? 'Request cancelled.' : errorText(error) } : turn));
    } finally { active.current = false; controller.current = null; }
  }
  async function loadHistory(id: string) {
    if (!id || active.current || historyBusy) return;
    setHistoryBusy(true); setHistoryError('');
    try {
      const result = await api<{ id: string; messages: { role: string; content: string; language: string; sources: Source[]; createdAt: string; isMock?: boolean; responseKind?: string }[] }>(`/ai/conversations/${id}`);
      if (!mounted.current) return;
      const history: Turn[] = [];
      for (const item of result.messages) {
        if (item.role === 'user') history.push({ question: item.content });
        else if (history.length) history[history.length - 1].response = { answer: item.content, language: item.language, sources: item.sources, generatedAtUtc: item.createdAt, requestId: '', conversationId: id, isMock: item.isMock, responseKind: item.responseKind };
      }
      setTurns(history); setConversationId(id); setMessage('');
    } catch (error) { if (mounted.current) setHistoryError(errorText(error)); }
    finally { if (mounted.current) setHistoryBusy(false); }
  }
  async function clearConversation() {
    if (active.current || historyBusy) return;
    setHistoryBusy(true); setHistoryError('');
    try {
      if (conversationId) await api(`/ai/conversations/${conversationId}`, { method: 'DELETE' });
      if (mounted.current) { setTurns([]); setConversationId(undefined); if (remember) void saved.refetch(); }
    } catch (error) { if (mounted.current) setHistoryError(errorText(error)); }
    finally { if (mounted.current) setHistoryBusy(false); }
  }
  return <section className="ai-page">
    <div className="flex flex-wrap items-start justify-between gap-4"><div><p className="eyebrow">{t('BUSINESS INSIGHTS')}</p><h1 className="text-3xl font-semibold tracking-tight">{t('AI Assistant')}</h1><p className="mt-3 max-w-2xl text-sm leading-6 text-slate-500">{t('Ask about sales, orders and inventory using authorized ERP reports.')}</p></div><span className="ai-badge">{t('Read only')}</span></div>
    <p className="ai-disclosure">{t('Your question and selected report data may be sent to the configured AI provider. Do not include secrets or personal information. Verify the source reports.')}</p>
    {status.data?.isMock && <p className="error-box" role="status">{t('Development mock — no live language model is connected.')}</p>}
    <div className="panel my-4 space-y-3">
      <p className="text-sm">{status.data?.provider}</p>
      <label className="flex items-center gap-2">Agent
        <select className="input" value={agent} disabled={mutation.isPending || historyBusy} onChange={e => { setAgent(e.target.value); setConversationId(undefined); setTurns([]); }}>
          <option value="reporting">{language === 'tr' ? 'Tüm raporlar' : 'All reports'}</option>
          <option value="inventory">{language === 'tr' ? 'Stok asistanı' : 'Inventory Agent'}</option>
        </select>
      </label>
      <label className="flex items-center gap-2"><input type="checkbox" checked={remember} disabled={mutation.isPending || historyBusy} onChange={event => { setRemember(event.target.checked); setConversationId(undefined); setTurns([]); setHistoryError(''); }} />{t('Save conversation history on the server')}</label>
      <p className="text-sm text-slate-500">{t('History is off by default. Saved conversations are private to your account and expire after the retention period.')} {t('Retention days')}: {status.data?.retentionDays ?? 7}</p>
      {remember && <label className="block text-sm">{t('Saved conversations')}<select className="input mt-2 w-full" value={conversationId ?? ''} disabled={mutation.isPending || historyBusy} onChange={event => void loadHistory(event.target.value)}><option value="">{t('Choose a saved conversation')}</option>{saved.data?.map(item => <option key={item.id} value={item.id}>{new Date(item.createdAt).toLocaleString(language === 'tr' ? 'tr-TR' : 'en-US')} · {item.turns}/10</option>)}</select></label>}
      {(historyBusy || (remember && saved.isFetching)) && <p role="status">{t('Loading conversation history…')}</p>}
      {(historyError || (remember && saved.error)) && <p role="alert" className="error-box">{historyError || errorText(saved.error)}</p>}
    </div>
    {status.isPending && <p role="status">{t('Checking availability…')}</p>}
    {status.error && <p className="error-box" role="alert">{errorText(status.error)}</p>}
    {status.data && !status.data.available && <div className="panel border-amber-200" role="status"><h2 className="font-semibold">{t('AI Assistant unavailable')}</h2><p className="mt-2 text-sm text-slate-500">{t(status.data.enabled ? 'AI provider configuration is required. Contact your administrator.' : 'AI is disabled. Your other ERP features remain available.')}</p></div>}
    <div className="ai-grid"><div className="panel ai-conversation">
      <div className="flex items-center justify-between border-b border-slate-100 pb-4"><h2 className="font-semibold">{t('Conversation')}</h2><button className="text-sm text-slate-500 disabled:opacity-40" disabled={mutation.isPending || historyBusy || (turns.length === 0 && !conversationId)} onClick={() => void clearConversation()}>{t('Clear conversation')}</button></div>
      <div className="ai-turns" aria-live="polite" aria-busy={mutation.isPending}>
        {turns.length === 0 && <div className="ai-empty"><span className="ai-mark" aria-hidden="true">✦</span><h2 className="mt-5 text-xl font-semibold">{t('Start with a business question')}</h2><p className="mt-3 text-sm leading-6 text-slate-500">{t('Choose an example or ask a specific question. Enable saved history to continue a conversation.')}</p></div>}
        {turns.map((turn, index) => <div key={index} className="ai-turn"><div className="ai-user"><p className="mb-2 text-xs font-semibold text-emerald-800">{t('You')}</p><p className="whitespace-pre-wrap break-words">{turn.question}</p></div>
          {turn.response && <article className="ai-answer"><p className="mb-2 text-xs font-semibold text-slate-500">{t(turn.response.responseKind === 'fallback' ? 'Verified ERP summary — AI explanation unavailable' : turn.response.isMock ? 'Development mock' : turn.response.sources.length ? turn.response.responseKind === 'deterministic' ? 'Verified ERP summary' : 'AI interpretation of ERP reports' : 'General guidance — no ERP report data')}</p><AiResponseText text={turn.response.answer} />
            {turn.response.sources.length > 0 && <div className="ai-sources"><h3 className="text-xs font-semibold uppercase tracking-wide text-slate-500">{t('Source reports')}</h3>{turn.response.sources.map((source, i) => <div key={i} className="mt-2 text-sm">{reportPaths.has(source.reportPath) ? <Link className="text-emerald-800 underline underline-offset-4" to={source.reportPath}>{t(source.label)}</Link> : <span>{t(source.label)}</span>}{source.parameters.startDate && source.parameters.endDate && <p className="mt-1 break-words text-xs text-slate-500">{source.parameters.startDate} → {source.parameters.endDate} {'(UTC)'}</p>}</div>)}</div>}
            <p className="mt-4 text-xs text-slate-400">{t('Generated at')} {new Date(turn.response.generatedAtUtc).toLocaleString(language === 'tr' ? 'tr-TR' : 'en-US')} · {t('Verify figures in the source reports.')}</p></article>}
          {turn.error && <p className="error-box mt-3" role="alert">{t(turn.error)}</p>}
        </div>)}
        {mutation.isPending && <p role="status" className="py-5 text-sm text-emerald-800">{t('Reading authorized reports…')}</p>}
      </div>
      <form className="ai-composer" onSubmit={event => { event.preventDefault(); void submit(); }}>
        <label className="mb-2 block text-sm font-medium" htmlFor="ai-question">{t('Your business question')}</label>
        <textarea id="ai-question" rows={3} maxLength={2000} value={message} onChange={event => setMessage(event.target.value)} disabled={!status.data?.available || mutation.isPending || historyBusy} placeholder={t('Ask about sales, orders or inventory…')} className="input w-full resize-y" />
        <div className="mt-3 flex items-center justify-between gap-3"><span className="text-xs text-slate-400">{message.length}/2000</span><div className="flex gap-3">{mutation.isPending && <button className="button-secondary" type="button" onClick={() => controller.current?.abort()}>{t('Cancel')}</button>}<button className="button" type="submit" disabled={!message.trim() || mutation.isPending || historyBusy || !status.data?.available}>{t('Send question')}</button></div></div>
      </form>
    </div><aside className="ai-aside"><div className="panel"><h2 className="font-semibold">{t('Try asking')}</h2><div className="mt-4 grid gap-2">{questions.map(question => <button key={question} className="ai-suggestion" disabled={!status.data?.available || mutation.isPending || historyBusy} onClick={() => setMessage(t(question))}>{t(question)}<span aria-hidden="true">↗</span></button>)}</div></div><p className="px-2 text-xs leading-6 text-slate-500">{t('Read-only tools cannot modify ERP records. Sales use completion dates; stock is current. Clear conversation deletes the selected saved history; execution metadata has separate retention.')}</p></aside></div>
  </section>;
}
