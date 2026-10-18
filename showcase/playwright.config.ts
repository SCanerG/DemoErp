import {defineConfig} from '@playwright/test';
export default defineConfig({
 testDir:'./tests/browser',fullyParallel:true,workers:2,retries:0,forbidOnly:!!process.env.CI,
 timeout:30000,expect:{timeout:7000},reporter:[['list'],['html',{open:'never'}],['json',{outputFile:'verification/browser-results.json'}]],
 use:{baseURL:'http://127.0.0.1:3400',channel:process.env.PLAYWRIGHT_CHANNEL||'chromium',actionTimeout:10000,navigationTimeout:15000,trace:'retain-on-failure',screenshot:'only-on-failure'},
 webServer:{command:'npm run preview',url:'http://127.0.0.1:3400',reuseExistingServer:false,timeout:30000},
});