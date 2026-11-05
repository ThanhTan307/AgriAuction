import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));

async function verify() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext();
  const page = await context.newPage();

  const csrfRes = await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf');
  const csrf = await csrfRes.json();

  await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: seed.Enterprise.email, password: seed.Enterprise.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });

  const auctionsRes = await context.request.get('http://127.0.0.1:5084/api/v1/auctions?page=1&pageSize=10');
  const auctions = await auctionsRes.json();
  console.log('Available auctions:', auctions.items.map(a => ({ id: a.id, title: a.content.title, state: a.state })));

  const firstAuction = auctions.items[0];

  // 1. Desktop 1440px
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`http://127.0.0.1:5084/pages/business/auction-room.html?auctionId=${firstAuction.id}`, { waitUntil: 'networkidle' });
  await page.waitForTimeout(600);
  await page.screenshot({ path: '.local/screenshots/room-closed-1440.png', fullPage: true });
  console.log('Saved room-closed-1440.png');

  // 2. Tablet 1024px
  await page.setViewportSize({ width: 1024, height: 768 });
  await page.waitForTimeout(400);
  await page.screenshot({ path: '.local/screenshots/room-closed-1024.png', fullPage: true });
  console.log('Saved room-closed-1024.png');

  // 3. Tablet 768px
  await page.setViewportSize({ width: 768, height: 1024 });
  await page.waitForTimeout(400);
  await page.screenshot({ path: '.local/screenshots/room-closed-768.png', fullPage: true });
  console.log('Saved room-closed-768.png');

  // 4. Mobile 390px
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(400);
  await page.screenshot({ path: '.local/screenshots/room-closed-390.png', fullPage: true });
  console.log('Saved room-closed-390.png');

  // 5. Test segmented filter click
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.locator('#bids-mine').click();
  await page.waitForTimeout(300);
  await page.screenshot({ path: '.local/screenshots/room-filter-mine-1440.png', fullPage: false });
  console.log('Saved room-filter-mine-1440.png');
  await page.locator('#bids-all').click();
  await page.waitForTimeout(300);

  // If second auction exists, check its view
  if (auctions.items.length > 1) {
    const secondAuction = auctions.items[1];
    await page.goto(`http://127.0.0.1:5084/pages/business/auction-room.html?auctionId=${secondAuction.id}`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(600);
    await page.screenshot({ path: '.local/screenshots/room-second-1440.png', fullPage: true });
    console.log('Saved room-second-1440.png');

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(400);
    await page.screenshot({ path: '.local/screenshots/room-second-390.png', fullPage: true });
    console.log('Saved room-second-390.png');
  }

  await browser.close();
}

verify().catch(console.error);
