import { chromium } from '@playwright/test';
import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));

async function inspect() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext();
  const csrfRes = await context.request.get('http://127.0.0.1:5084/api/v1/auth/csrf');
  const csrf = await csrfRes.json();
  await context.request.post('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    data: { email: seed.Enterprise.email, password: seed.Enterprise.password },
    headers: { 'X-CSRF-TOKEN': csrf.requestToken }
  });

  const auctionsRes = await context.request.get('http://127.0.0.1:5084/api/v1/auctions?page=1&pageSize=10');
  const auctions = await auctionsRes.json();
  const aId = auctions.items[0].id;

  const roomRes = await context.request.get(`http://127.0.0.1:5084/api/v1/auctions/${aId}/room`);
  const room = await roomRes.json();

  console.log('ROOM KEYS:', Object.keys(room));
  console.log('ROOM LEADER:', room.leader);
  console.log('ROOM RESULT:', JSON.stringify(room.result));
  console.log('ROOM BIDS[0]:', JSON.stringify(room.bids[0]));

  // Let's check single auction detail:
  const aDetailRes = await context.request.get(`http://127.0.0.1:5084/api/v1/auctions/${aId}`);
  const aDetail = await aDetailRes.json();
  console.log('A DETAIL KEYS:', Object.keys(aDetail));

  // Let's check if there is an endpoint for transactions or registrations that has enterprise name
  const transRes = await context.request.get('http://127.0.0.1:5084/api/v1/transactions?page=1&pageSize=10');
  const trans = await transRes.json();
  console.log('TRANS ITEMS:', trans.items?.map(t => ({ id: t.id, auctionId: t.auctionId, title: t.title })));
  if (trans.items?.length) {
    const tDetailRes = await context.request.get(`http://127.0.0.1:5084/api/v1/transactions/${trans.items[0].id}`);
    const tDetail = await tDetailRes.json();
    console.log('T DETAIL SNAPSHOT:', JSON.stringify(tDetail.snapshot));
    console.log('T DETAIL TRANSACTION:', JSON.stringify(tDetail.transaction));
  }

  // Check me
  const meRes = await context.request.get('http://127.0.0.1:5084/api/v1/me');
  const me = await meRes.json();
  console.log('ME:', JSON.stringify(me));

  await browser.close();
}

inspect().catch(console.error);

