import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';

// Generated, ignored, disposable demo credentials. Never commit or print them.
const credentials = JSON.parse(fs.readFileSync('.local/demo-credentials.json', 'utf8').replace(/^\uFEFF/, ''));
const postgres = JSON.parse(fs.readFileSync('.local/postgres-test/state.json', 'utf8').replace(/^\uFEFF/, ''));
const code = () => 'UI-' + crypto.randomUUID().replaceAll('-', '').slice(0, 20).toUpperCase();

async function login(page) {
  await page.goto('/pages/admin/category-management.html');
  await expect(page).toHaveURL(/\/pages\/login\.html/);
  await page.getByLabel('Email', { exact: true }).fill(credentials.email);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(credentials.password);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page).toHaveURL(/\/pages\/admin\/category-management\.html$/);
  await expect(page.locator('#category-rows')).toHaveAttribute('aria-busy', 'false');
  await expect(page.locator('#category-connection')).toHaveText('Đã cập nhật');
  await expect(page.locator('.app-sidebar')).toHaveCSS('position', 'fixed');
  await page.evaluate(async () => { await document.fonts.ready; });
}

async function fill(page, categoryCode, name) {
  await page.getByRole('button', { name: 'Thêm Danh Mục Mới' }).click();
  await page.getByLabel('Mã danh mục', { exact: true }).fill(categoryCode);
  await page.getByLabel('Tên danh mục', { exact: true }).fill(name);
}

async function save(page, status) {
  const response = page.waitForResponse(r => r.url().includes('/api/v1/admin/catalog/categories') && ['POST', 'PATCH'].includes(r.request().method()));
  await page.getByRole('button', { name: 'Lưu danh mục', exact: true }).click();
  expect((await response).status()).toBe(status);
}

function persisted(categoryCode) {
  // Generated code contains only A-Z/0-9/hyphen. Read-only SQL against the
  // explicitly isolated demo schema; no resets, DELETE, DROP or real-server credentials.
  if (!/^[A-Z0-9-]+$/.test(categoryCode)) throw new Error('Invalid test key');
  const sql = `SELECT json_build_object('name',name,'active',active,'version',version) FROM agri_demo.categories WHERE code='${categoryCode}'`;
  const stdout = execFileSync(process.env.AGRI_PSQL || 'C:\\Program Files\\PostgreSQL\\18\\bin\\psql.exe',
    ['-h', '127.0.0.1', '-p', String(postgres.port), '-U', postgres.username, '-d', 'agri_phase1_test', '-w', '-tAc', sql],
    { encoding: 'utf8', windowsHide: true, env: { ...process.env, PGPASSWORD: postgres.password, PGCLIENTENCODING: 'UTF8' } });
  return stdout.trim() ? JSON.parse(stdout.trim()) : null;
}

test('Admin login → real API → PostgreSQL → create/edit/inactivate → reload', async ({ page }) => {
  await login(page);
  const categoryCode = code();
  await fill(page, categoryCode, 'Danh mục kiểm thử trình duyệt');
  await save(page, 201);
  await expect(page.locator('#category-notice')).toHaveAttribute('data-kind', 'success');
  await page.locator('#category-search').fill(categoryCode);
  const row = page.locator('#category-rows tr').filter({ hasText: categoryCode });
  await expect(row).toContainText('Danh mục kiểm thử trình duyệt');
  expect(persisted(categoryCode)).toEqual({ name: 'Danh mục kiểm thử trình duyệt', active: true, version: 1 });
  await page.reload();
  await page.locator('#category-search').fill(categoryCode);
  await row.getByRole('button', { name: 'Sửa', exact: true }).click();
  await page.getByLabel('Tên danh mục', { exact: true }).fill('Danh mục E2E đã cập nhật');
  await page.getByLabel('Trạng thái', { exact: true }).selectOption('false');
  await save(page, 200);
  await expect(row).toContainText('Ngừng sử dụng');
  expect(persisted(categoryCode)).toEqual({ name: 'Danh mục E2E đã cập nhật', active: false, version: 2 });
  await page.screenshot({ path: 'TestResults/category-desktop.png', fullPage: true });
  await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click();
  await expect(page).toHaveURL(/\/pages\/login\.html$/);
  await page.goto('/pages/admin/category-management.html');
  await expect(page).toHaveURL(/\/pages\/login\.html/);
});

test('Real backend validation and unique constraint never show false success', async ({ page }) => {
  await login(page);
  const categoryCode = code();
  await fill(page, categoryCode, 'Bản ghi gốc');
  await save(page, 201);
  await expect(page.locator('#category-dialog')).not.toBeVisible();
  await fill(page, categoryCode, 'Bản ghi bị trùng');
  await save(page, 409);
  await expect(page.locator('[data-field-error="code"]')).toContainText('đã được sử dụng');
  await expect(page.locator('#category-dialog')).toBeVisible();
  await expect(page.locator('#category-notice')).not.toBeVisible();
  expect(persisted(categoryCode).name).toBe('Bản ghi gốc');
  await page.getByLabel('Mã danh mục', { exact: true }).fill(code());
  await page.getByLabel('Tên danh mục', { exact: true }).fill('   ');
  await save(page, 400);
  await expect(page.locator('[data-field-error="name"]')).toContainText('120');
  await expect(page.locator('#category-notice')).not.toBeVisible();
});

test('Explicit network fault shows Error and reload recovers real data', async ({ page }) => {
  await login(page);
  await page.route('**/api/v1/admin/catalog/categories?*', route => route.abort('failed'), { times: 1 });
  await page.getByRole('button', { name: 'Tải lại dữ liệu', exact: true }).click();
  await expect(page.locator('#category-state')).toHaveAttribute('data-kind', 'error');
  await expect(page.locator('#category-total')).toHaveText('—');
  await expect(page.locator('#category-notice')).not.toBeVisible();
  await page.getByRole('button', { name: 'Tải lại dữ liệu', exact: true }).click();
  await expect(page.locator('#category-connection')).toHaveText('Đã cập nhật');
});

test('Small viewport keeps category controls accessible', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(page);
  await expect(page.getByRole('button', { name: 'Thêm Danh Mục Mới' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
  await page.screenshot({ path: 'TestResults/category-mobile.png', fullPage: true });
});
