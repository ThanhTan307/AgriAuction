import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const enterpriseCreds = seed.Enterprise;

const pages = [
  { name: 'market-explorer', url: 'http://127.0.0.1:5084/pages/business/market-explorer.html' },
  { name: 'deposit-wallet', url: 'http://127.0.0.1:5084/pages/business/deposit-wallet.html' },
  { name: 'notifications', url: 'http://127.0.0.1:5084/pages/notifications.html' },
  { name: 'dispute-claim', url: 'http://127.0.0.1:5084/pages/business/dispute-claim.html' },
  { name: 'profile', url: 'http://127.0.0.1:5084/pages/profile.html' }
];

async function run() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext();
  const page = await context.newPage();

  const csrfRes = await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf');
  const csrf = await csrfRes.json();

  await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: enterpriseCreds.email, password: enterpriseCreds.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });

  for (const item of pages) {
    for (const vp of [{ w: 1440, h: 900, l: '1440' }, { w: 390, h: 844, l: '390' }]) {
      await page.setViewportSize({ width: vp.w, height: vp.h });
      await page.goto(item.url, { waitUntil: 'networkidle' });
      await page.waitForTimeout(300);
      await page.screenshot({ path: `.local/screenshots/enterprise-after/${item.name}-${vp.l}.png`, fullPage: true });
      console.log(`Captured ${item.name} ${vp.l}`);
    }
  }

  await browser.close();
}

run().catch(console.error);

