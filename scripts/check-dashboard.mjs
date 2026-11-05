import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const adminCreds = seed.Administrator;

async function checkDashboard() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext();
  const csrfRes = await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf');
  const csrf = await csrfRes.json();
  await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: adminCreds.email, password: adminCreds.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });

  const today = '2026-11-04';
  const from = '2026-10-01';
  const res = await context.request.get(`http://127.0.0.1:5084/api/v1/admin/reports/dashboard?from=${from}&through=${today}&timeZone=Asia/Ho_Chi_Minh`);
  const data = await res.json();
  console.log('Values:', JSON.stringify(data.values, null, 2));
  console.log('WinningAuctions:', data.winningAuctions, 'NoWinner:', data.noWinnerAuctions, 'WinnerRate:', data.winnerRatePercent);
  console.log('Completion report:');
  const compRes = await context.request.get(`http://127.0.0.1:5084/api/v1/me/reports/completion?from=${from}&through=${today}`);
  if (compRes.ok()) {
    console.log(await compRes.json());
  } else {
    console.log('Completion status:', compRes.status());
  }

  // Also check what page renders
  const page = await context.newPage();
  await page.goto('http://127.0.0.1:5084/pages/admin/analytics-dashboard.html');
  await page.waitForTimeout(1500);

  const chartState = await page.textContent('#chart-state');
  console.log('Chart state text:', chartState);
  const chartSvg = await page.locator('#value-chart svg').count();
  console.log('Chart SVG count:', chartSvg);

  await browser.close();
}
checkDashboard().catch(console.error);

