import fs from 'node:fs';
import { test, expect, loginAPI, newContext, closeContexts } from './support/diagnostics.mjs';
import { PostgresProof } from './support/postgres-proof.mjs';

const credentials = JSON.parse(fs.readFileSync('.local/phase2-demo-credentials.json', 'utf8').replace(/^\uFEFF/, '')).Farmer;
let api, storageState, db;
test.beforeAll(async () => {
  api = await loginAPI(credentials);
  storageState = await api.storageState();
  db = new PostgresProof();
});
test.afterAll(async () => {
  try {
    if (api) {
      try { expect((await api.post('/api/v1/auth/logout', { data: {} })).status()).toBe(204); }
      finally { await api.dispose(); }
    }
  } finally { await db?.close(); }
});

test('Farmer category pagination contract loads choices beyond the first 100', async ({ browser }) => {
  const context = await newContext(browser, { storageState });
  try {
    const page = await context.newPage(), requestedPages = [];
    const categories = Array.from({ length: 102 }, (_, i) => ({
      id: `00000000-0000-4000-8000-${String(i + 1).padStart(12, '0')}`,
      code: `CATEGORY_${String(i + 1).padStart(3, '0')}`, name: `Danh mục hợp đồng ${i + 1}`,
      active: true, version: 1, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z'
    }));
    // Isolated HTTP contract fixture, never inserted into PostgreSQL.
    await page.route('**/api/v1/catalog/categories?*', route => {
      const url = new URL(route.request().url()), number = Number(url.searchParams.get('page'));
      requestedPages.push(number);
      return route.fulfill({ status: 200, json: {
        items: categories.slice((number - 1) * 100, number * 100), totalCount: 102,
        allCount: 102, activeCount: 102, inactiveCount: 0, page: number, pageSize: 100
      } });
    });
    await page.goto('/pages/farmer/lots.html');
    await expect(page.locator('#lot-new')).toBeEnabled();
    await expect(page.locator('#lot-category option[value]:not([value=""])')).toHaveCount(102);
    expect(requestedPages).toEqual([1, 2]);
    await page.locator('#lot-category').selectOption(categories.at(-1).id);
    await expect(page.locator('#lot-category')).toHaveValue(categories.at(-1).id);
  } finally { await closeContexts([context]); }
});

test('Farmer category choices match all active PostgreSQL categories and include XOAI_DEMO when present', async ({ browser }) => {
  const categories = []; let number = 1, response;
  do {
    const result = await api.get(`/api/v1/catalog/categories?page=${number++}&pageSize=100`);
    expect(result.status()).toBe(200); response = await result.json(); categories.push(...response.items);
    if (!response.items.length && categories.length < response.totalCount) throw new Error('Unexpected empty catalogue page');
  } while (categories.length < response.totalCount);
  const sql = await db.query("SELECT json_build_object('activeCount',count(*),'xoai',(SELECT row_to_json(x) FROM (SELECT id,code,name,position FROM (SELECT id,code,name,row_number() OVER (ORDER BY code) position FROM agri_demo.categories WHERE active) ordered WHERE code='XOAI_DEMO') x)) FROM agri_demo.categories WHERE active");
  expect(categories.length).toBe(Number(sql.activeCount));
  const context = await newContext(browser, { storageState });
  try {
    const page = await context.newPage(); await page.goto('/pages/farmer/lots.html');
    await expect(page.locator('#lot-new')).toBeEnabled();
    const choices = page.locator('#lot-category option[value]:not([value=""])');
    await expect(choices).toHaveCount(categories.length);
    const actual = await choices.evaluateAll(options => options.map(option => ({ id: option.value, name: option.textContent })));
    expect(actual).toEqual(categories.map(c => ({ id: c.id, name: c.name })));
    if (sql.xoai) {
      await page.locator('#lot-category').selectOption(sql.xoai.id);
      await expect(page.locator('#lot-category')).toHaveValue(sql.xoai.id);
      await expect(page.locator('#lot-category option:checked')).toHaveText(sql.xoai.name);
    }
    fs.writeFileSync('TestResults/farmer-category-pagination-proof.json', JSON.stringify({
      at: new Date().toISOString(), database: '127.0.0.1:55432/agri_phase1_test',
      apiTotalCount: response.totalCount, sql, uiChoices: actual.length,
      allChoicesMatchApi: true, xoaiSelectable: !!sql.xoai, businessDataWritten: false
    }, null, 2));
  } finally { await closeContexts([context]); }
});
