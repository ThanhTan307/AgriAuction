import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const adminCreds = seed.Administrator;

fs.mkdirSync('.local/screenshots/after', { recursive: true });

async function testPrep() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();

  // 1. Login
  const csrfRes = await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf');
  const csrf = await csrfRes.json();
  const loginRes = await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: adminCreds.email, password: adminCreds.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });
  console.log('Login:', loginRes.status());

  // 2. Open auction preparation
  await page.goto('http://127.0.0.1:5084/pages/admin/auction-preparation.html');
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(1000);

  // Check overflow at 1440
  let overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  console.log('1440px overflow:', overflow);
  await page.screenshot({ path: '.local/screenshots/after/prep-1440-tab1-auctions.png', fullPage: true });

  // Test Tab 2: Registrations
  await page.locator('#tab-btn-registrations').click();
  await page.waitForTimeout(500);
  await page.screenshot({ path: '.local/screenshots/after/prep-1440-tab2-registrations.png', fullPage: true });

  // Check disabled confirmation buttons
  const disabledButtons = await page.locator('#registration-rows button:disabled').count();
  console.log('Disabled confirm buttons (due to expired session window):', disabledButtons);

  // Test Tab 3: Create Form
  await page.locator('#tab-btn-create').click();
  await page.waitForTimeout(500);
  await page.screenshot({ path: '.local/screenshots/after/prep-1440-tab3-create.png', fullPage: true });

  // Test responsive sizes
  const viewports = [
    { w: 1024, h: 768, name: '1024' },
    { w: 768, h: 1024, name: '768' },
    { w: 390, h: 844, name: '390' },
    { w: 360, h: 800, name: '360' },
    { w: 320, h: 640, name: '320' },
  ];

  for (const vp of viewports) {
    await page.setViewportSize({ width: vp.w, height: vp.h });
    await page.waitForTimeout(400);

    // Switch to registrations tab
    await page.locator('#tab-btn-registrations').click();
    await page.waitForTimeout(300);
    overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    console.log(`${vp.name}px overflow (registrations tab):`, overflow);
    await page.screenshot({ path: `.local/screenshots/after/prep-${vp.name}-registrations.png`, fullPage: true });

    // Switch to auctions tab
    await page.locator('#tab-btn-auctions').click();
    await page.waitForTimeout(300);
    overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    console.log(`${vp.name}px overflow (auctions tab):`, overflow);
    await page.screenshot({ path: `.local/screenshots/after/prep-${vp.name}-auctions.png`, fullPage: true });
  }

  await browser.close();
  console.log('Auction preparation tests completed successfully!');
}

testPrep().catch(console.error);

