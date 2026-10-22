// Explicit opt-in; exercises authenticated DemoERP, never calls Ollama directly.
import { runLiveSmoke } from './ai-live-smoke.mjs';
if (process.env.OLLAMA_LIVE_SMOKE !== 'true') {
  console.log('SKIP: live Ollama smoke requires OLLAMA_LIVE_SMOKE=true. No network request was made.');
} else {
  runLiveSmoke({ ...process.env, AI_LIVE_PROVIDER: 'Ollama', AI_LIVE_SMOKE: 'true' }).catch(() => {
    console.error('Live Ollama check failed. Review safe server errors and local configuration.');
    process.exitCode = 1;
  });
}
