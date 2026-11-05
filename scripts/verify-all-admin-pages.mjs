import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const adminCreds = seed.Administrator;

const adminPages = [
  { name: 'preparation', url: 'http://127.0.0.1:5084/pages/admin/auction-preparation.html' },
  { name: 'dashboard', url: 'http://127.0.0.1:5084/pages/admin/analytics-dashboard.html' },
  { name: 'accounts', url: 'http://127.0.0.1:5084/pages/admin/accounts.html' },
  { name: 'lot-inspection', url: 'http://127.0.0.1:5084/pages/admin/lot-inspection.html' },
  { name: 'categories', url: 'http://127.0.0.1:5084/pages/admin/category-management.html' },
  { name: 'disputes', url: 'http://127.0.0.1:5084/pages/admin/dispute-arbitration.html' },
  { name: 'transactions', url: 'http://127.0.0.1:5084/pages/transactions.html' },
  { name: 'notifications', url: 'http://127.0.0.1:5084/pages/notifications.html' },
  { name: 'profile', url: 'http://127.0.0.1:5084/pages/profile.html' },
  { name: 'auction-room', url: 'http://127.0.0.1:5084/pages/business/auction-room.html' },
];

const viewports = [
  { name: '1440', width: 1440, height: 900 },
  { name: '1024', width: 1024, height: 768 },
  { name: '768', width: 768, height: 1024 },
  { name: '390', width: 390, height: 844 },
  { name: '360', width: 360, height: 800 },
  { name: '320', width: 320, height: 640 },
];

fs.mkdirSync('.local/screenshots/verify', { recursive: true });

async function verifyAll() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext();
  const page = await context.newPage();

  const pageErrors = [];
  page.on('pageerror', err => pageErrors.push({ url: page.url(), err: err.message }));

  // Login
  const csrfRes = await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf');
  const csrf = await csrfRes.json();
  const loginRes = await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: adminCreds.email, password: adminCreds.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });
  console.log('Admin login status:', loginRes.status());

  const results = [];

  for (const p of adminPages) {
    console.log(`Verifying: ${p.name}`);
    try {
      await page.goto(p.url, { waitUntil: 'domcontentloaded', timeout: 10000 });
      await page.waitForTimeout(600);

      for (const vp of viewports) {
        await page.setViewportSize({ width: vp.width, height: vp.height });
        await page.waitForTimeout(200);

        const check = await page.evaluate(() => {
          const docW = document.documentElement.scrollWidth;
          const winW = window.innerWidth;
          const overflow = docW > winW;
          return { docW, winW, overflow };
        });

        if (check.overflow) {
          console.warn(`  [OVERFLOW] ${p.name} at ${vp.name}px: docW=${check.docW}, winW=${check.winW}`);
        }

        results.push({ page: p.name, vp: vp.name, overflow: check.overflow });

        if (vp.name === '1440' || vp.name === '390') {
          await page.screenshot({ path: `.local/screenshots/verify/${p.name}-${vp.name}.png`, fullPage: true });
        }
      }
    } catch (err) {
      console.error(`  Error verifying ${p.name}:`, err.message);
    }
  }

  await browser.close();

  const overflows = results.filter(r => r.overflow);
  console.log('\n--- VERIFICATION SUMMARY ---');
  console.log('Total viewport checks:', results.length);
  console.log('Overflow failures:', overflows.length);
  if (overflows.length > 0) {
    console.log('Failed viewports:', JSON.stringify(overflows, null, 2));
  } else {
    console.log('All viewports passed zero-overflow check!');
  }
  console.log('Page JS errors count:', pageErrors.length);
  if (pageErrors.length > 0) {
    console.log('Errors:', pageErrors);
  }
}

verifyAll().catch(console.error);
