import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const adminCreds = seed.Administrator;

async function run() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();

  const csrf = await (await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf')).json();
  await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: adminCreds.email, password: adminCreds.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });

  await page.goto('http://127.0.0.1:5084/pages/admin/category-management.html#checklist-kiem-duyet');
  await page.waitForTimeout(400);

  // Test PATCH
  const saveBtn = await page.locator('.workflow-catalog-save').first();
  await saveBtn.click();
  await page.waitForTimeout(500);

  let msg = await page.evaluate(() => {
    const el = document.querySelector('.p2-notice');
    return { text: el?.textContent, hidden: el?.hidden, kind: el?.dataset.kind };
  });
  console.log('PATCH response notice:', msg);

  // Test POST
  const testTitle = 'Tiêu chí kiểm định tự động ' + Date.now();
  await page.fill('#catalog-Inspection', testTitle);
  await page.click('.workflow-catalog-create button[type="submit"]');
  await page.waitForTimeout(600);

  msg = await page.evaluate(() => {
    const el = document.querySelector('.p2-notice');
    return { text: el?.textContent, hidden: el?.hidden, kind: el?.dataset.kind };
  });
  console.log('POST response notice:', msg);

  const found = await page.evaluate((val) => {
    const inputs = Array.from(document.querySelectorAll('.workflow-col-title input'));
    return inputs.some(i => i.value === val);
  }, testTitle);
  console.log('New item in list:', found);

  await browser.close();
}

run().catch(console.error);
