import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));
const adminCreds = seed.Administrator;

async function checkData() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext();
  const csrfRes = await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf');
  const csrf = await csrfRes.json();
  await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: adminCreds.email, password: adminCreds.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });

  const txRes = await context.request.get('http://127.0.0.1:5084/api/v1/transactions?page=1&pageSize=10');
  const txData = await txRes.json();
  console.log('Transactions:', JSON.stringify(txData.items, null, 2));

  const notifRes = await context.request.get('http://127.0.0.1:5084/api/v1/me/notifications?page=1&pageSize=10');
  const notifData = await notifRes.json();
  console.log('Notifications:', JSON.stringify(notifData.items, null, 2));

  const depRes = await context.request.get('http://127.0.0.1:5084/api/v1/admin/deposit-settlements');
  if (depRes.ok()) {
    console.log('Deposit settlements:', JSON.stringify(await depRes.json(), null, 2));
  }

  await browser.close();
}
checkData().catch(console.error);

