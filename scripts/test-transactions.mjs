import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const enterpriseCreds = seed.Enterprise;

async function run() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();

  const csrfRes = await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf');
  const csrf = await csrfRes.json();

  await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: enterpriseCreds.email, password: enterpriseCreds.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });

  const transRes = await context.request.get('http://127.0.0.1:5084/api/v1/transactions?page=1&pageSize=10');
  const trans = await transRes.json();
  const firstTran = trans.items[0];

  await page.goto(`http://127.0.0.1:5084/pages/transactions.html?transactionId=${firstTran.id}`, { waitUntil: 'networkidle' });
  await page.waitForTimeout(600);
  await page.screenshot({ path: '.local/screenshots/test-transactions-1440.png', fullPage: true });
  console.log('Saved test-transactions-1440.png');

  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(400);
  await page.screenshot({ path: '.local/screenshots/test-transactions-390.png', fullPage: true });
  console.log('Saved test-transactions-390.png');

  await browser.close();
}

run().catch(console.error);

