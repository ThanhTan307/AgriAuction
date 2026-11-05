import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const enterpriseCreds = seed.Enterprise;

const pages = [
  { name: 'market-explorer', url: 'http://127.0.0.1:5084/pages/business/market-explorer.html' },
  { name: 'deposit-wallet', url: 'http://127.0.0.1:5084/pages/business/deposit-wallet.html' },
  { name: 'auction-room', url: 'http://127.0.0.1:5084/pages/business/auction-room.html' },
  { name: 'transactions', url: 'http://127.0.0.1:5084/pages/transactions.html' },
  { name: 'notifications', url: 'http://127.0.0.1:5084/pages/notifications.html' },
  { name: 'dispute-claim', url: 'http://127.0.0.1:5084/pages/business/dispute-claim.html' },
  { name: 'profile', url: 'http://127.0.0.1:5084/pages/profile.html' }
];

const viewports = [
  { width: 1440, height: 900, label: '1440' },
  { width: 1024, height: 768, label: '1024' },
  { width: 768, height: 1024, label: '768' },
  { width: 390, height: 844, label: '390' },
  { width: 360, height: 800, label: '360' },
  { width: 320, height: 640, label: '320' }
];

fs.mkdirSync('.local/screenshots/enterprise-after', { recursive: true });

async function run() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();

  // 1. Get CSRF token
  const csrfRes = await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf');
  const csrf = await csrfRes.json();

  // 2. Login as Enterprise
  const loginRes = await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: enterpriseCreds.email, password: enterpriseCreds.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });
  console.log('Login status:', loginRes.status());

  // Get active auction and transaction
  const auctionsRes = await context.request.get('http://127.0.0.1:5084/api/v1/auctions?page=1&pageSize=10');
  const auctions = await auctionsRes.json();
  const auctionId = auctions.items?.[0]?.id;

  const transRes = await context.request.get('http://127.0.0.1:5084/api/v1/transactions?page=1&pageSize=10');
  const trans = await transRes.json();
  const transId = trans.items?.[0]?.id;

  // Capture auction-room with selected auction across all viewports
  if (auctionId) {
    for (const vp of viewports) {
      await page.setViewportSize({ width: vp.width, height: vp.height });
      await page.goto(`http://127.0.0.1:5084/pages/business/auction-room.html?auctionId=${auctionId}`, { waitUntil: 'networkidle' });
      await page.waitForTimeout(600);
      await page.screenshot({ path: `.local/screenshots/enterprise-after/auction-room-${vp.label}.png`, fullPage: true });
      console.log(`Captured auction-room @ ${vp.label}px`);
    }
  }

  // Capture transactions with selected transaction across all viewports
  if (transId) {
    for (const vp of viewports) {
      await page.setViewportSize({ width: vp.width, height: vp.height });
      await page.goto(`http://127.0.0.1:5084/pages/transactions.html?transactionId=${transId}`, { waitUntil: 'networkidle' });
      await page.waitForTimeout(600);
      await page.screenshot({ path: `.local/screenshots/enterprise-after/transactions-${vp.label}.png`, fullPage: true });
      console.log(`Captured transactions @ ${vp.label}px`);
    }
  }

  // Capture other pages at 1440 and 390
  for (const item of pages) {
    if (item.name === 'auction-room' || item.name === 'transactions') continue;
    for (const vp of [{ width: 1440, height: 900, label: '1440' }, { width: 390, height: 844, label: '390' }]) {
      await page.setViewportSize({ width: vp.width, height: vp.height });
      await page.goto(item.url, { waitUntil: 'networkidle' });
      await page.waitForTimeout(500);
      await page.screenshot({ path: `.local/screenshots/enterprise-after/${item.name}-${vp.label}.png`, fullPage: true });
      console.log(`Captured ${item.name} @ ${vp.label}px`);
    }
  }

  await browser.close();
  console.log('ALL SCREENSHOTS CAPTURED SUCCESSFULLY!');
}

run().catch(console.error);

