import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const auctionId = '03ca6dac-65bd-4c3c-be98-742134dc70d4';

async function run() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });

  // 1. Enterprise 1 (Doanh nghiệp An Phú)
  console.log('--- Checking Enterprise 1 (An Phú) ---');
  const ctx1 = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page1 = await ctx1.newPage();
  const csrf1 = await (await ctx1.request.get('http://127.0.0.1:5084/api/v1/auth/csrf')).json();
  await ctx1.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: seed.Enterprise.email, password: seed.Enterprise.password },
    headers: { 'X-CSRF-TOKEN': csrf1.requestToken }
  });
  await page1.goto(`http://127.0.0.1:5084/pages/business/auction-room.html?auctionId=${auctionId}`, { waitUntil: 'networkidle' });
  await page1.waitForTimeout(600);

  const e1Bidders = await page1.locator('#bid-history-tbody tr .col-bidder').allTextContents();
  const e1Result = await page1.locator('#result-text').textContent();
  const e1Leader = await page1.locator('#room-leader').textContent();
  console.log('Enterprise 1 bidders:', e1Bidders);
  console.log('Enterprise 1 result:', e1Result);
  console.log('Enterprise 1 leader:', e1Leader);
  await page1.screenshot({ path: '.local/screenshots/verify-enterprise1.png', fullPage: true });

  // 2. Enterprise 2 (Doanh nghiệp Bình Minh)
  console.log('--- Checking Enterprise 2 (Bình Minh) ---');
  const ctx2 = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page2 = await ctx2.newPage();
  const csrf2 = await (await ctx2.request.get('http://127.0.0.1:5084/api/v1/auth/csrf')).json();
  await ctx2.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: seed.Enterprise2.email, password: seed.Enterprise2.password },
    headers: { 'X-CSRF-TOKEN': csrf2.requestToken }
  });
  await page2.goto(`http://127.0.0.1:5084/pages/business/auction-room.html?auctionId=${auctionId}`, { waitUntil: 'networkidle' });
  await page2.waitForTimeout(600);

  const e2Bidders = await page2.locator('#bid-history-tbody tr .col-bidder').allTextContents();
  const e2Result = await page2.locator('#result-text').textContent();
  const e2Leader = await page2.locator('#room-leader').textContent();
  console.log('Enterprise 2 bidders:', e2Bidders);
  console.log('Enterprise 2 result:', e2Result);
  console.log('Enterprise 2 leader:', e2Leader);
  await page2.screenshot({ path: '.local/screenshots/verify-enterprise2.png', fullPage: true });

  // 3. Administrator (Quản trị viên)
  console.log('--- Checking Administrator ---');
  const ctxAdmin = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const pageAdmin = await ctxAdmin.newPage();
  const csrfAdmin = await (await ctxAdmin.request.get('http://127.0.0.1:5084/api/v1/auth/csrf')).json();
  await ctxAdmin.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: seed.Administrator.email, password: seed.Administrator.password },
    headers: { 'X-CSRF-TOKEN': csrfAdmin.requestToken }
  });
  await pageAdmin.goto(`http://127.0.0.1:5084/pages/business/auction-room.html?auctionId=${auctionId}`, { waitUntil: 'networkidle' });
  await pageAdmin.waitForTimeout(600);

  const adminBidders = await pageAdmin.locator('#bid-history-tbody tr .col-bidder').allTextContents();
  const adminResult = await pageAdmin.locator('#result-text').textContent();
  const adminLeader = await pageAdmin.locator('#room-leader').textContent();
  console.log('Admin bidders:', adminBidders);
  console.log('Admin result:', adminResult);
  console.log('Admin leader:', adminLeader);
  await pageAdmin.screenshot({ path: '.local/screenshots/verify-admin.png', fullPage: true });

  await browser.close();
}

run().catch(console.error);

