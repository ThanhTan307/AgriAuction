import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const adminCreds = seed.Administrator;

async function run() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();

  // 1. Get CSRF token
  const csrfRes = await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf');
  const csrf = await csrfRes.json();

  // 2. Login
  await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: adminCreds.email, password: adminCreds.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });

  // Go to category-management
  await page.goto('http://127.0.0.1:5084/pages/admin/category-management.html#checklist-kiem-duyet', { waitUntil: 'networkidle' });
  await page.waitForTimeout(1000);
  await page.screenshot({ path: '.local/screenshots/tab2-current.png', fullPage: false });

  await page.goto('http://127.0.0.1:5084/pages/admin/category-management.html#ly-do-khieu-nai', { waitUntil: 'networkidle' });
  await page.waitForTimeout(1000);
  await page.screenshot({ path: '.local/screenshots/tab3-current.png', fullPage: false });

  console.log('Screenshots saved to .local/screenshots/tab2-current.png and tab3-current.png');
  await browser.close();
}

run().catch(console.error);

