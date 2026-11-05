import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const adminCreds = seed.Administrator;

const pages = [
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

fs.mkdirSync('.local/screenshots/before', { recursive: true });

async function run() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();

  // 1. Get CSRF token
  const csrfRes = await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf');
  const csrf = await csrfRes.json();
  console.log('CSRF Token received');

  // 2. Login
  const loginRes = await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: adminCreds.email, password: adminCreds.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });
  console.log('Login status:', loginRes.status());

  // Check me
  const meRes = await context.request.get('http://127.0.0.1:5084/api/v1/me');
  console.log('Me status:', meRes.status(), await meRes.json());

  // Capture each page at desktop (1440) and mobile (390)
  for (const item of pages) {
    try {
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.goto(item.url, { waitUntil: 'networkidle', timeout: 15000 });
      await page.waitForTimeout(1000);
      await page.screenshot({ path: `.local/screenshots/before/${item.name}-1440.png`, fullPage: true });

      await page.setViewportSize({ width: 390, height: 844 });
      await page.waitForTimeout(500);
      await page.screenshot({ path: `.local/screenshots/before/${item.name}-390.png`, fullPage: true });

      console.log(`Captured ${item.name}`);
    } catch (err) {
      console.error(`Failed to capture ${item.name}:`, err.message);
    }
  }

  await browser.close();
  console.log('Done capturing screenshots');
}

run().catch(console.error);

