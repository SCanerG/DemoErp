import{defineConfig}from'@playwright/test';
export default defineConfig({testDir:'./tests',testMatch:'workflows-live.spec.ts',workers:1,reporter:'list',use:{baseURL:process.env.FRONTEND_URL??'http://127.0.0.1:3040',channel:process.env.PLAYWRIGHT_CHANNEL??'msedge',headless:true,trace:'off',screenshot:'off'}});
