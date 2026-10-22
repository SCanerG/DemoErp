// Opt-in live acceptance check. Never seeds records, changes business data, or prints credentials/report bodies.
import { pathToFileURL } from 'node:url';

export async function runLiveSmoke(env = process.env, write = console.log) {
  const provider = env.AI_LIVE_PROVIDER ?? 'OpenAI';
  if (!['OpenAI', 'Ollama'].includes(provider)) throw new Error('Unsupported live provider.');
  if (env.AI_LIVE_SMOKE !== 'true') { write('SKIP: live provider smoke requires AI_LIVE_SMOKE=true. No network request was made.'); return { skipped: true }; }
  const base = new URL(env.API_URL ?? 'http://localhost:5080');
  if (base.username || base.password || base.search || base.hash || !['', '/'].includes(base.pathname)
    || !(base.protocol === 'https:' || (base.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(base.hostname))))
    throw new Error('API_URL must be an HTTPS API origin or a loopback HTTP origin, without embedded credentials.');
  const email = env.AI_LIVE_EMAIL, password = env.AI_LIVE_PASSWORD;
  if (!email || !password) throw new Error('Set AI_LIVE_EMAIL and AI_LIVE_PASSWORD locally for an existing ERP account.');
  const deadline = AbortSignal.timeout(150000);
  async function request(path, { data, token } = {}) {
    const response = await fetch(new URL('/api' + path, base), {
      method: data ? 'POST' : 'GET', signal: deadline, redirect: 'error',
      headers: { ...(data ? { 'Content-Type': 'application/json' } : {}), ...(token ? { Authorization: 'Bearer ' + token } : {}) },
      ...(data ? { body: JSON.stringify(data) } : {}),
    });
    const value = await response.json().catch(() => null);
    if (!response.ok) throw new Error(`Live check failed at ${path}: HTTP ${response.status}. Review the server using its safe correlation ID.`);
    return value;
  }
  const login = await request('/auth/login', { data: { email, password } });
  if (typeof login?.accessToken !== 'string' || !login.accessToken) throw new Error('Authentication did not return a session.');
  const token = login.accessToken;
  const status = await request('/ai/status', { token });
  if (!status?.available || status.isMock || status.provider !== provider) throw new Error('An enabled, configured selected real provider is required; Mock does not count as live integration.');
  const report = await request('/reports/inventory?stockStatus=Low&pageSize=5', { token });
  if (!Array.isArray(report?.items) || !Number.isInteger(report.totalCount)) throw new Error('Inventory report contract could not be verified.');
  const answer = await request('/ai/chat', { token, data: { message: 'Which products currently have low inventory?', language: 'tr', ...(provider === 'Ollama' ? { agent: 'inventory' } : {}) } });
  if (answer?.isMock || answer?.language !== 'tr' || typeof answer.answer !== 'string' || !answer.answer.trim()
    || !Array.isArray(answer.sources) || !answer.sources.some(s => s.name === 'get_low_stock_products' && s.reportPath === '/reports/inventory')
    || !/^[0-9a-f-]{36}$/i.test(answer.executionId ?? '')) throw new Error('Live acceptance failed: Turkish answer and verified low-stock source are required.');
  const execution = await request('/ai/executions/' + answer.executionId, { token });
  if (execution?.provider !== provider || !execution.model || execution.outcome !== 'success' || execution.correlationId !== answer.requestId
    || !Number.isInteger(execution.totalTokens) || execution.totalTokens < 1 || !Array.isArray(execution.tools)
    || !execution.tools.some(t => t.name === 'get_low_stock_products' && t.outcome === 'success')) throw new Error('Live execution metadata could not be verified.');
  const result = { provider, model: execution.model, liveProviderVerified: true, responseKind: answer.responseKind, authenticated: true, language: answer.language, readOnlyTool: 'get_low_stock_products',
    actualLowStockRowsAtPrecheck: report.totalCount, executionId: execution.id, correlationId: execution.correlationId, totalTokens: execution.totalTokens,
    note: report.totalCount === 0 ? 'No low-stock rows existed at precheck; no sample rows were created.' : 'Review the Turkish answer against current report figures; automated checks do not certify every model statement.' };
  write(JSON.stringify(result)); return result;
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  runLiveSmoke().catch(() => { console.error('Live smoke failed. Check local configuration, ERP credentials and safe server error categories. No secrets or provider bodies are printed.'); process.exitCode = 1; });
}
