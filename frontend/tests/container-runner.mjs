// Forward the app's local URLs inside this test container. This keeps the actual
// frontend bundle and its CORS origin identical to the evaluator's browser.
import net from 'node:net';
import { spawn } from 'node:child_process';

const servers = [];
for (const [port, host] of [
  [Number(new URL(process.env.FRONTEND_URL ?? 'http://localhost:3000').port), 'frontend'],
  [Number(new URL(process.env.API_URL ?? 'http://localhost:5080').port), 'backend'],
]) {
  const server = net.createServer(client => {
    const target = net.connect(8080, host);
    client.pipe(target).pipe(client);
    target.on('error', () => client.destroy());
    client.on('error', () => target.destroy());
    client.on('close', () => target.destroy());
  });
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, '0.0.0.0', resolve);
  });
  servers.push(server);
}
const tests = process.env.PORTFOLIO_CAPTURE
  ? spawn('node', ['scripts/capture-screenshots.mjs'], { stdio: 'inherit' })
  : spawn('npm', ['run', 'test:e2e', '--', ...process.argv.slice(2)], { stdio: 'inherit' });
tests.on('error', error => { console.error(error.message); process.exit(1); });
tests.on('exit', code => { servers.forEach(server => server.close()); process.exit(code ?? 1); });
