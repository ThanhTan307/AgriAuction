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

  await page.goto('http://127.0.0.1:5084/pages/admin/category-management.html#checklist-kiem-duyet', { waitUntil: 'networkidle' });
  await page.waitForTimeout(500);

  const metrics = await page.evaluate(() => {
    const form = document.querySelector('.workflow-catalog-create');
    const input = form?.querySelector('input');
    const btn = form?.querySelector('button');
    const firstRow = document.querySelector('.workflow-catalog-row');
    const rowInput = firstRow?.querySelector('input:not([type="checkbox"])');
    const rowActive = firstRow?.querySelector('.p2-checkbox-label');
    const rowSave = firstRow?.querySelector('.workflow-catalog-save');

    return {
      createForm: form ? {
        computedGrid: window.getComputedStyle(form).gridTemplateColumns,
        width: form.getBoundingClientRect().width,
      } : null,
      createInput: input ? {
        width: input.getBoundingClientRect().width,
      } : null,
      createBtn: btn ? {
        width: btn.getBoundingClientRect().width,
      } : null,
      row: firstRow ? {
        computedGrid: window.getComputedStyle(firstRow).gridTemplateColumns,
        width: firstRow.getBoundingClientRect().width,
      } : null,
      rowInput: rowInput ? {
        width: rowInput.getBoundingClientRect().width,
        scrollWidth: rowInput.scrollWidth,
        clientWidth: rowInput.clientWidth,
      } : null,
      rowActive: rowActive ? {
        width: rowActive.getBoundingClientRect().width,
      } : null,
      rowSave: rowSave ? {
        width: rowSave.getBoundingClientRect().width,
      } : null,
    };
  });

  console.log('Metrics:', JSON.stringify(metrics, null, 2));
  await browser.close();
}

run().catch(console.error);

