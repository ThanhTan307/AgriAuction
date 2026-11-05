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

fs.mkdirSync('.local/screenshots/enterprise-before', { recursive: true });

async function run() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();

  // 1. Get CSRF token
  const csrfRes = await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf');
  const csrf = await csrfRes.json();
  console.log('CSRF Token received');

  // 2. Login as Enterprise
  const loginRes = await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: enterpriseCreds.email, password: enterpriseCreds.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });
  console.log('Login status:', loginRes.status());

  for (const item of pages) {
    try {
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.goto(item.url, { waitUntil: 'networkidle', timeout: 15000 });
      await page.waitForTimeout(600);
      await page.screenshot({ path: `.local/screenshots/enterprise-before/${item.name}-1440.png`, fullPage: true });
      console.log(`Captured 1440: ${item.name}`);

      await page.setViewportSize({ width: 390, height: 844 });
      await page.waitForTimeout(300);
      await page.screenshot({ path: `.local/screenshots/enterprise-before/${item.name}-390.png`, fullPage: true });
      console.log(`Captured 390: ${item.name}`);
    } catch (e) {
      console.error(`Error capturing ${item.name}:`, e.message);
    }
  }

  // Also check if auction-room can select an auction if not selected
  // Let's check available auctions
  const auctionsRes = await context.request.get('http://127.0.0.1:5084/api/v1/auctions?page=1&pageSize=10');
  const auctions = await auctionsRes.json();
  console.log('Auctions available:', auctions.items?.length);
  if (auctions.items?.length) {
    const firstAuction = auctions.items[0];
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`http://127.0.0.1:5084/pages/business/auction-room.html?auctionId=${firstAuction.id}`, { waitUntil: 'networkidle', timeout: 15000 });
    await page.waitForTimeout(1000);
    await page.screenshot({ path: `.local/screenshots/enterprise-before/auction-room-selected-1440.png`, fullPage: true });
    console.log(`Captured auction-room with auction ${firstAuction.id}`);
  }

  // Also check transactions detail selected
  const transRes = await context.request.get('http://127.0.0.1:5084/api/v1/transactions?page=1&pageSize=10');
  const trans = await transRes.json();
  console.log('Transactions available:', trans.items?.length);
  if (trans.items?.length) {
    const firstTran = trans.items[0];
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`http://127.0.0.1:5084/pages/transactions.html?transactionId=${firstTran.id}`, { waitUntil: 'networkidle', timeout: 15000 });
    await page.waitForTimeout(1000);
    await page.screenshot({ path: `.local/screenshots/enterprise-before/transactions-selected-1440.png`, fullPage: true });
    console.log(`Captured transactions with transaction ${firstTran.id}`);
  }

  await browser.close();
}

run().catch(console.error);

