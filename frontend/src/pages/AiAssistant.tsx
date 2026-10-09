import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api, errorText } from '../api';
import { t, useLanguage } from '../i18n';

type Source = { type: string; name: string; label: string; parameters: { startDate?: string; endDate?: string }; reportPath: string };
type Answer = { answer: string; language: string; generatedAtUtc: string; sources: Source[]; requestId: string };
type Turn = { question: string; response?: Answer; error?: string };
const questions = ['Summarize sales for the last 30 days.', 'Show the top five selling products.', 'Which products have low stock?', 'How many orders are pending?', 'Who are our top customers?'];
const reportPaths = new Set(['/reports/sales', '/reports/products', '/reports/customers', '/reports/inventory', '/orders']);

export default function AiAssistant() {
  const language = useLanguage();
  const [message, setMessage] = useState('');
  const [turns, setTurns] = useState<Turn[]>([]);
  const controller = useRef<AbortController | null>(null);
  const active = useRef(false);
  const mounted = useRef(true);
  const status = useQuery({ queryKey: ['ai-status'], queryFn: ({ signal }) => api<{ enabled: boolean; configured: boolean; available: boolean }>('/ai/status', { signal }), retry: false });
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; controller.current?.abort(); }; }, []);
  const mutation = useMutation({ meta: { affectsBusinessData: false }, retry: false,
    mutationFn: ({ question, signal, selectedLanguage }: { question: string; signal: AbortSignal; selectedLanguage: string }) =>
      api<Answer>('/ai/chat', { method: 'POST', body: JSON.stringify({ message: question, language: selectedLanguage }), signal }),
  });
  async function submit() {
    const question = message.trim();
    if (!question || question.length > 2000 || active.current || !status.data?.available) return;
    active.current = true;
    const request = new AbortController(); controller.current = request;
    setTurns(previous => [...previous.slice(-19), { question }]); setMessage('');
    try {
      const response = await mutation.mutateAsync({ question, signal: request.signal, selectedLanguage: language });
      if (mounted.current && !request.signal.aborted) setTurns(previous => previous.map((turn, i) => i === previous.length - 1 ? { ...turn, response } : turn));
    } catch (error) {
      if (mounted.current) setTurns(previous => previous.map((turn, i) => i === previous.length - 1 ? { ...turn, error: request.signal.aborted ? 'Request cancelled.' : errorText(error) } : turn));
    } finally { active.current = false; controller.current = null; }
  }
  return <section className="ai-page">
    <div className="flex flex-wrap items-start justify-between gap-4"><div><p className="eyebrow">{t('BUSINESS INSIGHTS')}</p><h1 className="text-3xl font-semibold tracking-tight">{t('AI Assistant')}</h1><p className="mt-3 max-w-2xl text-sm leading-6 text-slate-500">{t('Ask about sales, orders and inventory using authorized ERP reports.')}</p></div><span className="ai-badge">{t('Read only')}</span></div>
    <p className="ai-disclosure">{t('Your question and selected report data may be sent to OpenAI. Do not include secrets or personal information. Answers may be inaccurate; verify the source reports.')}</p>
    {status.isPending && <p role="status">{t('Checking availability…')}</p>}
    {status.error && <p className="error-box" role="alert">{errorText(status.error)}</p>}
    {status.data && !status.data.available && <div className="panel border-amber-200" role="status"><h2 className="font-semibold">{t('AI Assistant unavailable')}</h2><p className="mt-2 text-sm text-slate-500">{t(status.data.enabled ? 'AI provider configuration is required. Contact your administrator.' : 'AI is disabled. Your other ERP features remain available.')}</p></div>}
    <div className="ai-grid"><div className="panel ai-conversation">
      <div className="flex items-center justify-between border-b border-slate-100 pb-4"><h2 className="font-semibold">{t('Conversation')}</h2><button className="text-sm text-slate-500 disabled:opacity-40" disabled={mutation.isPending || turns.length === 0} onClick={() => setTurns([])}>{t('Clear conversation')}</button></div>
      <div className="ai-turns" aria-live="polite" aria-busy={mutation.isPending}>
        {turns.length === 0 && <div className="ai-empty"><span className="ai-mark" aria-hidden="true">✦</span><h2 className="mt-5 text-xl font-semibold">{t('Start with a business question')}</h2><p className="mt-3 text-sm leading-6 text-slate-500">{t('Choose an example or ask a specific question. Each request is independent; include dates and context.')}</p></div>}
        {turns.map((turn, index) => <div key={index} className="ai-turn"><div className="ai-user"><p className="mb-2 text-xs font-semibold text-emerald-800">{t('You')}</p><p className="whitespace-pre-wrap break-words">{turn.question}</p></div>
          {turn.response && <article className="ai-answer"><p className="mb-2 text-xs font-semibold text-slate-500">{t('AI Assistant')}</p><p className="whitespace-pre-wrap break-words leading-7">{turn.response.answer}</p>
            {turn.response.sources.length > 0 && <div className="ai-sources"><h3 className="text-xs font-semibold uppercase tracking-wide text-slate-500">{t('Source reports')}</h3>{turn.response.sources.map((source, i) => <div key={i} className="mt-2 text-sm">{reportPaths.has(source.reportPath) ? <Link className="text-emerald-800 underline underline-offset-4" to={source.reportPath}>{t(source.label)}</Link> : <span>{t(source.label)}</span>}{source.parameters.startDate && source.parameters.endDate && <p className="mt-1 break-words text-xs text-slate-500">{source.parameters.startDate} → {source.parameters.endDate} {'(UTC)'}</p>}</div>)}</div>}
            <p className="mt-4 text-xs text-slate-400">{t('Generated at')} {new Date(turn.response.generatedAtUtc).toLocaleString(language === 'tr' ? 'tr-TR' : 'en-US')} · {t('Verify figures in the source reports.')}</p></article>}
          {turn.error && <p className="error-box mt-3" role="alert">{t(turn.error)}</p>}
        </div>)}
        {mutation.isPending && <p role="status" className="py-5 text-sm text-emerald-800">{t('Reading authorized reports…')}</p>}
      </div>
      <form className="ai-composer" onSubmit={event => { event.preventDefault(); void submit(); }}>
        <label className="mb-2 block text-sm font-medium" htmlFor="ai-question">{t('Your business question')}</label>
        <textarea id="ai-question" rows={3} maxLength={2000} value={message} onChange={event => setMessage(event.target.value)} disabled={!status.data?.available || mutation.isPending} placeholder={t('Ask about sales, orders or inventory…')} className="input w-full resize-y" />
        <div className="mt-3 flex items-center justify-between gap-3"><span className="text-xs text-slate-400">{message.length}/2000</span><div className="flex gap-3">{mutation.isPending && <button className="button-secondary" type="button" onClick={() => controller.current?.abort()}>{t('Cancel')}</button>}<button className="button" type="submit" disabled={!message.trim() || mutation.isPending || !status.data?.available}>{t('Send question')}</button></div></div>
      </form>
    </div><aside className="ai-aside"><div className="panel"><h2 className="font-semibold">{t('Try asking')}</h2><div className="mt-4 grid gap-2">{questions.map(question => <button key={question} className="ai-suggestion" disabled={!status.data?.available || mutation.isPending} onClick={() => setMessage(t(question))}>{t(question)}<span aria-hidden="true">↗</span></button>)}</div></div><p className="px-2 text-xs leading-6 text-slate-500">{t('Read-only tools cannot modify records. Sales use completion dates; stock is current. Conversations reset on refresh and are not saved.')}</p></aside></div>
  </section>;
}
