import fs from 'node:fs';
import { test, expect, loginAPI, newContext, closeContexts } from './support/diagnostics.mjs';

const viewports = [{ width: 1440, height: 900 }, { width: 768, height: 1024 }, { width: 390, height: 844 }, { width: 1280, height: 500 }];
const styles = ['fonts.generated', 'tailwind.generated', 'app-theme.generated', 'category-management', 'phase2', 'phase3'].map(name => `<link rel="stylesheet" href="/assets/css/${name}.css">`).join('');
async function bounds(page, selector) {
  const geometry = await page.locator(selector).evaluate(dialog => {
    const rect = dialog.getBoundingClientRect();
    return { x: rect.x, y: rect.y, width: rect.width, height: rect.height, viewportWidth: innerWidth, viewportHeight: innerHeight, scrollHeight: dialog.scrollHeight, clientHeight: dialog.clientHeight };
  });
  expect(Math.abs(geometry.x + geometry.width / 2 - geometry.viewportWidth / 2)).toBeLessThanOrEqual(2);
  expect(Math.abs(geometry.y + geometry.height / 2 - geometry.viewportHeight / 2)).toBeLessThanOrEqual(2);
  expect(geometry.x).toBeGreaterThanOrEqual(0); expect(geometry.y).toBeGreaterThanOrEqual(0);
  expect(geometry.x + geometry.width).toBeLessThanOrEqual(geometry.viewportWidth + 1);
  expect(geometry.y + geometry.height).toBeLessThanOrEqual(geometry.viewportHeight + 1);
  return geometry;
}

test('Shared dialog layout contract centers short and long content at desktop tablet mobile and short height', async ({ browser }) => {
  const context = await newContext(browser), results = [];
  try {
    const page = await context.newPage(); await page.goto('/pages/login.html');
    for (const className of ['category-dialog', 'p3-dialog', 'p2-photo-dialog']) {
      for (const long of [false, true]) {
        const content = long ? '<p>Nội dung hồ sơ dài để kiểm tra cuộn bên trong.</p>'.repeat(80) : '<p>Nội dung ngắn.</p>';
        await page.setContent(`<!doctype html><html><head>${styles}</head><body data-page="phase3"><button id="opener" onclick="document.querySelector('dialog').showModal()">Mở</button><dialog class="${className}"><button autofocus type="button" onclick="this.closest('dialog').close()">Đóng</button>${content}<button id="last-action" type="button">Cuối hồ sơ</button></dialog></body></html>`, { waitUntil: 'load' });
        for (const viewport of viewports) {
          await page.setViewportSize(viewport); await page.locator('#opener').click();
          await expect(page.locator('dialog')).toBeVisible();
          const geometry = await bounds(page, 'dialog');
          if (long) expect(geometry.scrollHeight).toBeGreaterThan(geometry.clientHeight);
          await page.locator('#last-action').scrollIntoViewIfNeeded(); await expect(page.locator('#last-action')).toBeInViewport();
          await page.keyboard.press('Escape'); await expect(page.locator('dialog')).toBeHidden();
          await expect(page.locator('#opener')).toBeFocused();
          results.push({ className, long, ...geometry });
        }
      }
    }
    fs.writeFileSync('TestResults/dialog-layout-contract.json', JSON.stringify({ at: new Date().toISOString(), results }, null, 2));
  } finally { await closeContexts([context]); }
});

test('Real category auction details and nested photo dialogs remain centered with existing data', async ({ browser }) => {
  const read = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
  const accounts = [['admin', read('.local/demo-credentials.json')], ['enterprise', read('.local/phase2-demo-credentials.json').Enterprise]];
  const results = [];
  for (const [role, credentials] of accounts) {
    let api, context;
    try {
      api = await loginAPI(credentials); context = await newContext(browser, { storageState: await api.storageState() });
      const page = await context.newPage();
      await page.goto(role === 'admin' ? '/pages/admin/category-management.html' : '/pages/business/market-explorer.html');
      if (role === 'admin') { await expect(page.locator('#category-connection')).toHaveText('Đã cập nhật'); await page.locator('#category-add').click(); }
      else { await page.locator('#market-grid .p3-auction-card button').first().click(); await expect(page.locator('#auction-dialog .p2-gallery img').first()).toBeVisible(); }
      const selector = role === 'admin' ? '#category-dialog' : '#auction-dialog';
      for (const viewport of viewports.slice(0, 3)) {
        await page.setViewportSize(viewport);
        await page.waitForFunction(() => document.fonts.status === 'loaded' && [...document.querySelectorAll('dialog[open] img[src]')].every(i => i.complete && i.naturalWidth > 0), null, { timeout: 8000 });
        const geometry = await bounds(page, selector); results.push({ role, ...geometry });
        await page.screenshot({ path: `TestResults/dialog-${role}-${viewport.width}.png`, fullPage: false, animations: 'disabled' });
      }
      if (role === 'enterprise') {
        await page.locator('#auction-dialog .p2-gallery img').first().click();
        await expect(page.locator('#photo-dialog')).toBeVisible();
        await page.waitForFunction(() => document.querySelector('#photo-dialog img')?.complete && document.querySelector('#photo-dialog img')?.naturalWidth > 0, null, { timeout: 8000 });
        results.push({ role: 'nested-photo', ...await bounds(page, '#photo-dialog') });
        await page.screenshot({ path: 'TestResults/dialog-photo-mobile.png', fullPage: false, animations: 'disabled' });
        await page.keyboard.press('Escape'); await expect(page.locator('#photo-dialog')).toBeHidden(); await expect(page.locator('#auction-dialog')).toBeVisible();
      }
      await page.keyboard.press('Escape'); await expect(page.locator(selector)).toBeHidden();
    } finally {
      await closeContexts([context]);
      if (api) { try { expect((await api.post('/api/v1/auth/logout', { data: {} })).status()).toBe(204); } finally { await api.dispose(); } }
    }
  }
  fs.writeFileSync('TestResults/dialog-layout-live.json', JSON.stringify({ at: new Date().toISOString(), results, businessDataWritten: false }, null, 2));
});
