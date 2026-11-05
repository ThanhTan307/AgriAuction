import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const adminCreds = seed.Administrator;

fs.mkdirSync('.local/screenshots/notifications', { recursive: true });

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

  await page.goto('http://127.0.0.1:5084/pages/notifications.html', { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(1000);

  // 1. Check auto-refresh explanation text is gone
  const autoRefreshTextPresent = await page.evaluate(() => {
    return document.body.innerText.includes('Tự tải lại mỗi 10 giây');
  });
  console.log('1. Auto-refresh text present in DOM (should be false):', autoRefreshTextPresent);

  // 2. Check "Đánh dấu tất cả đã đọc" visibility when unread is 0
  const markAllState = await page.evaluate(() => {
    const btn = document.getElementById('mark-all-read');
    const badge = document.getElementById('unread-count');
    return {
      btnHidden: btn.hidden || window.getComputedStyle(btn).display === 'none',
      badgeHidden: badge.hidden || window.getComputedStyle(badge).display === 'none',
    };
  });
  console.log('2. Mark all & badge hidden when unread=0:', markAllState);

  // 3. Check notification rows count & container
  const rowMetrics = await page.evaluate(() => {
    const container = document.getElementById('notification-list');
    const rows = Array.from(container.querySelectorAll('.p5-inbox-row'));
    return {
      rowCount: rows.length,
      heights: rows.map(r => r.getBoundingClientRect().height),
      isSingleContainer: container.classList.contains('p5-inbox-container'),
      titles: rows.map(r => r.querySelector('.p5-inbox-title')?.innerText),
      lots: rows.map(r => r.querySelector('.p5-inbox-lot')?.innerText),
      links: rows.map(r => r.querySelector('.p5-inbox-link')?.innerText),
      times: rows.map(r => r.querySelector('.p5-inbox-time')?.innerText),
    };
  });
  console.log('3. Notification metrics on 1440px:');
  console.log('   Row count:', rowMetrics.rowCount);
  console.log('   Row heights (target 68-88px):', rowMetrics.heights);
  console.log('   Sample titles:', rowMetrics.titles.slice(0, 2));
  console.log('   Sample lots:', rowMetrics.lots.slice(0, 2));
  console.log('   Sample links:', rowMetrics.links.slice(0, 2));
  console.log('   Sample times:', rowMetrics.times.slice(0, 2));

  // Screenshot at 1440px
  await page.screenshot({ path: '.local/screenshots/notifications/notifications-1440.png', fullPage: true });

  // 4. Test Viewports (1440, 1024, 390, 320)
  const viewports = [
    { w: 1440, h: 900, name: '1440' },
    { w: 1024, h: 768, name: '1024' },
    { w: 390, h: 844, name: '390' },
    { w: 320, h: 640, name: '320' },
  ];

  for (const vp of viewports) {
    await page.setViewportSize({ width: vp.w, height: vp.h });
    await page.waitForTimeout(300);

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    console.log(`4. Viewport ${vp.name}px overflow:`, overflow);

    await page.screenshot({ path: `.local/screenshots/notifications/notifications-${vp.name}.png`, fullPage: true });
  }

  await browser.close();
  console.log('All notification verification finished successfully!');
}

verify().catch(console.error);

