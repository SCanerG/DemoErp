import { defineConfig, devices } from '@playwright/test';

export default defineConfig({ testDir: './tests', fullyParallel: false, workers: 1,
  reporter: 'list', use: { baseURL: process.env.FRONTEND_URL ?? 'http://localhost:3000', trace: 'retain-on-failure', channel: 'chromium' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
