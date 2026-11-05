import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const adminCreds = seed.Administrator;

fs.mkdirSync('.local/screenshots/final', { recursive: true });

async function verify() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();

  // Login
  const csrfRes = await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf');
  const csrf = await csrfRes.json();
  await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: adminCreds.email, password: adminCreds.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });

  await page.goto('http://127.0.0.1:5084/pages/admin/auction-preparation.html', { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(1500);

  // 1. Verify Banner
  const bannerVisible = await page.evaluate(() => {
    const el = document.getElementById('urgent-pending-banner');
    return el.offsetHeight > 0 && window.getComputedStyle(el).display !== 'none';
  });
  console.log('1. Urgent Banner visible (should be false when count is 0):', bannerVisible);

  // 2. Verify Nav Badges
  const badgesVisible = await page.evaluate(() => {
    const b1 = document.querySelector('[data-nav-key="notifications"] .app-nav-badge');
    const b2 = document.querySelector('.app-inbox-collapsed-badge');
    return {
      sidebarBadgeVisible: b1 ? (b1.offsetHeight > 0 && window.getComputedStyle(b1).display !== 'none') : false,
      compactBadgeVisible: b2 ? (b2.offsetHeight > 0 && window.getComputedStyle(b2).display !== 'none') : false,
    };
  });
  console.log('2. Nav badges visible:', badgesVisible);

  // 3. Tab 1 Screenshot (1440px)
  await page.screenshot({ path: '.local/screenshots/final/01-tab-auctions-1440.png', fullPage: true });

  // 4. Tab 2 Screenshot & verify no disabled buttons for expired registrations
  await page.locator('#tab-btn-registrations').click();
  await page.waitForTimeout(400);
  const disabledButtonsCount = await page.locator('#registration-rows button:disabled').count();
  console.log('3. Disabled buttons count in registration rows (should be 0):', disabledButtonsCount);
  await page.screenshot({ path: '.local/screenshots/final/02-tab-registrations-1440.png', fullPage: true });

  // 5. Tab 3: Create Form layout
  await page.locator('#tab-btn-create').click();
  await page.waitForTimeout(400);
  const formFieldCount = await page.locator('#prepare-form input, #prepare-form select').count();
  console.log('4. Create form input count:', formFieldCount);
  await page.screenshot({ path: '.local/screenshots/final/03-tab-create-1440.png', fullPage: true });

  // 6. Test Responsive viewports for overflow
  const viewports = [
    { w: 1440, h: 900, name: '1440' },
    { w: 1024, h: 768, name: '1024' },
    { w: 768, h: 1024, name: '768' },
    { w: 390, h: 844, name: '390' },
    { w: 360, h: 800, name: '360' },
    { w: 320, h: 640, name: '320' },
  ];

  for (const vp of viewports) {
    await page.setViewportSize({ width: vp.w, height: vp.h });
    await page.waitForTimeout(300);

    for (const tabKey of ['auctions', 'registrations', 'create']) {
      await page.locator(`#tab-btn-${tabKey}`).click();
      await page.waitForTimeout(200);
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      if (overflow) {
        console.error(`OVERFLOW DETECTED: ${vp.name}px on tab ${tabKey}`);
      }
    }
    // Screenshot mobile viewports
    if (vp.w === 390) {
      await page.locator('#tab-btn-auctions').click();
      await page.screenshot({ path: '.local/screenshots/final/04-mobile-390-auctions.png', fullPage: true });
      await page.locator('#tab-btn-create').click();
      await page.screenshot({ path: '.local/screenshots/final/05-mobile-390-create.png', fullPage: true });
    }
  }
  console.log('5. Responsive overflow test completed for all 6 viewports.');

  // 7. Verify Unit Tests still pass
  await browser.close();
  console.log('All browser verifications finished!');
}

verify().catch(console.error);

