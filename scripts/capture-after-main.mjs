import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const enterpriseCreds = seed.Enterprise;

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

  const auctionsRes = await context.request.get('http://127.0.0.1:5084/api/v1/auctions?page=1&pageSize=10');
  const auctions = await auctionsRes.json();
  const auctionId = auctions.items[0].id;

  for (const vp of [{ w: 390, h: 844, l: '390' }, { w: 360, h: 800, l: '360' }, { w: 320, h: 640, l: '320' }]) {
    await page.setViewportSize({ width: vp.w, height: vp.h });
    await page.goto(`http://127.0.0.1:5084/pages/business/auction-room.html?auctionId=${auctionId}`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(300);
    await page.screenshot({ path: `.local/screenshots/enterprise-after/auction-room-${vp.l}.png`, fullPage: true });
    console.log(`Captured auction-room ${vp.l}`);
  }

  const transRes = await context.request.get('http://127.0.0.1:5084/api/v1/transactions?page=1&pageSize=10');
  const trans = await transRes.json();
  const transId = trans.items[0].id;

  for (const vp of [
    { w: 1440, h: 900, l: '1440' },
    { w: 1024, h: 768, l: '1024' },
    { w: 768, h: 1024, l: '768' },
    { w: 390, h: 844, l: '390' },
    { w: 360, h: 800, l: '360' },
    { w: 320, h: 640, l: '320' }
  ]) {
    await page.setViewportSize({ width: vp.w, height: vp.h });
    await page.goto(`http://127.0.0.1:5084/pages/transactions.html?transactionId=${transId}`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(300);
    await page.screenshot({ path: `.local/screenshots/enterprise-after/transactions-${vp.l}.png`, fullPage: true });
    console.log(`Captured transactions ${vp.l}`);
  }

  await browser.close();
}

run().catch(console.error);

