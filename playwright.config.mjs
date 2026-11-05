import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/browser',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 45000,
  globalTimeout: 300000,
  expect: { timeout: 12000 },
  outputDir: '.local/product/browser-artifacts',
  reporter: [['list'], ['json', { outputFile: process.env.AGRIAUCTION_E2E_RUN_DIR ? process.env.AGRIAUCTION_E2E_RUN_DIR+'/results.json' : '.local/product/browser-results.json' }]],
  use: {
    baseURL: 'http://127.0.0.1:5084',
    browserName: 'chromium',
    channel: 'msedge',
    headless: true,
    actionTimeout: 10000,
    navigationTimeout: 15000,
    viewport: { width: 1440, height: 1000 },
    trace: 'off',
    screenshot: 'only-on-failure'
  }
});
