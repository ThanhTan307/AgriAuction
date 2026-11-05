import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const adminCreds = seed.Administrator;

const viewports = [
  { name: '1440', width: 1440, height: 900 },
  { name: '1024', width: 1024, height: 768 },
  { name: '768', width: 768, height: 1024 },
  { name: '390', width: 390, height: 844 },
  { name: '320', width: 320, height: 568 }
];

async function run() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext();
  const page = await context.newPage();

  const csrfRes = await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf');
  const csrf = await csrfRes.json();

  await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: adminCreds.email, password: adminCreds.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });

  fs.mkdirSync('.local/screenshots/verify-tabs', { recursive: true });

  for (const vp of viewports) {
    await page.setViewportSize({ width: vp.width, height: vp.height });

    // Tab 2
    await page.goto('http://127.0.0.1:5084/pages/admin/category-management.html#checklist-kiem-duyet', { waitUntil: 'networkidle' });
    await page.waitForTimeout(400);
    await page.screenshot({ path: `.local/screenshots/verify-tabs/tab2-${vp.name}.png`, fullPage: false });

    // Check if horizontal scrollbar appears on body
    const tab2Scroll = await page.evaluate(() => {
      return document.documentElement.scrollWidth > document.documentElement.clientWidth;
    });

    // Tab 3
    await page.goto('http://127.0.0.1:5084/pages/admin/category-management.html#ly-do-khieu-nai', { waitUntil: 'networkidle' });
    await page.waitForTimeout(400);
    await page.screenshot({ path: `.local/screenshots/verify-tabs/tab3-${vp.name}.png`, fullPage: false });

    const tab3Scroll = await page.evaluate(() => {
      return document.documentElement.scrollWidth > document.documentElement.clientWidth;
    });

    console.log(`Viewport ${vp.name}px: Tab2 h-scroll: ${tab2Scroll}, Tab3 h-scroll: ${tab3Scroll}`);
  }

  // Also test editing a criterion and saving
  console.log('Testing PATCH action...');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('http://127.0.0.1:5084/pages/admin/category-management.html#checklist-kiem-duyet', { waitUntil: 'networkidle' });
  await page.waitForTimeout(400);

  const saveBtn = await page.$('.workflow-catalog-save');
  if (saveBtn) {
    await saveBtn.click();
    await page.waitForTimeout(600);
    const noticeText = await page.textContent('.p2-notice');
    console.log('Save result notice:', noticeText);
  }

  // Also test adding a new item and then verifying it loads
  console.log('Testing POST action...');
  const addInput = await page.$('#catalog-Inspection');
  const addBtn = await page.$('.workflow-catalog-create button[type="submit"]');
  if (addInput && addBtn) {
    const testName = 'Tiêu chí kiểm tra ngẫu nhiên ' + Date.now();
    await addInput.fill(testName);
    await addBtn.click();
    await page.waitForTimeout(800);
    const noticeText = await page.textContent('.p2-notice');
    console.log('Add result notice:', noticeText);

    // Verify it appears in the list
    const found = await page.evaluate((val) => {
      const inputs = Array.from(document.querySelectorAll('.workflow-col-title input'));
      return inputs.some(i => i.value === val);
    }, testName);
    console.log('Added item found in list:', found);
  }

  await browser.close();
}

run().catch(console.error);

