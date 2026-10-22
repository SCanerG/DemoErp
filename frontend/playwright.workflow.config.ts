import { defineConfig } from '@playwright/test';

// Explicit local write acceptance only; traces/screenshots must not capture login credentials.
export default defineConfig({
  testDir: './tests', testMatch: 'agent-center.spec.ts', grep: /Live approved purchase/,
  workers: 1, reporter: 'list',
  use: { baseURL: process.env.FRONTEND_URL ?? 'http://127.0.0.1:3025', channel: process.env.PLAYWRIGHT_CHANNEL ?? 'msedge',
    headless: true, trace: 'off', screenshot: 'off' },
});
