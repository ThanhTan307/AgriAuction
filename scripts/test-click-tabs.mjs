import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const adminCreds = seed.Administrator;

async function run() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();

  const csrfRes = await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf');
  const csrf = await csrfRes.json();

  await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: adminCreds.email, password: adminCreds.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });

  await page.goto('http://127.0.0.1:5084/pages/admin/category-management.html');
  await page.waitForTimeout(500);

  fs.mkdirSync('.local/screenshots/verify-tabs', { recursive: true });

  // 1. Click Tab 2: Tiêu chí kiểm duyệt
  await page.click('#tab-btn-inspection');
  await page.waitForTimeout(500);
  await page.screenshot({ path: '.local/screenshots/verify-tabs/tab2-1440.png', fullPage: false });
  console.log('Tab 2 1440px captured');

  // Check metrics on Tab 2
  const m2 = await page.evaluate(() => {
    const input = document.querySelector('.workflow-catalog-create input');
    const btn = document.querySelector('.workflow-catalog-create button');
    const firstRowInput = document.querySelector('.workflow-catalog-row input');
    const statusCol = document.querySelector('.workflow-col-status');
    const actionCol = document.querySelector('.workflow-col-actions');
    return {
      createInputW: input?.getBoundingClientRect().width,
      createBtnW: btn?.getBoundingClientRect().width,
      rowInputW: firstRowInput?.getBoundingClientRect().width,
      statusColW: statusCol?.getBoundingClientRect().width,
      actionColW: actionCol?.getBoundingClientRect().width,
    };
  });
  console.log('Tab 2 metrics:', m2);

  // 2. Click Tab 3: Lý do khiếu nại
  await page.click('#tab-btn-complaints');
  await page.waitForTimeout(500);
  await page.screenshot({ path: '.local/screenshots/verify-tabs/tab3-1440.png', fullPage: false });
  console.log('Tab 3 1440px captured');

  // Check metrics on Tab 3
  const m3 = await page.evaluate(() => {
    const input = document.querySelector('#tab-pane-complaints .workflow-catalog-create input');
    const btn = document.querySelector('#tab-pane-complaints .workflow-catalog-create button');
    const firstRowInput = document.querySelector('#tab-pane-complaints .workflow-catalog-row input');
    return {
      createInputW: input?.getBoundingClientRect().width,
      createBtnW: btn?.getBoundingClientRect().width,
      rowInputW: firstRowInput?.getBoundingClientRect().width,
    };
  });
  console.log('Tab 3 metrics:', m3);

  // Test mobile view on Tab 2
  await page.setViewportSize({ width: 390, height: 844 });
  await page.click('#tab-btn-inspection');
  await page.waitForTimeout(300);
  await page.screenshot({ path: '.local/screenshots/verify-tabs/tab2-390.png', fullPage: false });
  console.log('Tab 2 390px captured');

  // Test mobile view on Tab 3
  await page.click('#tab-btn-complaints');
  await page.waitForTimeout(300);
  await page.screenshot({ path: '.local/screenshots/verify-tabs/tab3-390.png', fullPage: false });
  console.log('Tab 3 390px captured');

  await browser.close();
}

run().catch(console.error);

