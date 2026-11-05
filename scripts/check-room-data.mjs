import fs from 'node:fs';

const seed = JSON.parse(fs.readFileSync('.local/product/seed.json', 'utf8'));

async function check() {
  const csrf = await (await fetch('http://127.0.0.1:5084/api/v1/auth/csrf')).json();
  const res = await fetch('http://127.0.0.1:5084/api/v1/auth/login?useCookies=true', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf.requestToken },
    body: JSON.stringify({ email: seed.Enterprise.email, password: seed.Enterprise.password })
  });
  // Note: multiple Set-Cookie headers in fetch need getSetCookie()
  const cookies = res.headers.getSetCookie().join('; ');
  console.log('Login cookies present:', Boolean(cookies));

  const auctionsRes = await fetch('http://127.0.0.1:5084/api/v1/auctions?page=1&pageSize=10', {
    headers: { Cookie: cookies }
  });
  const auctions = await auctionsRes.json();
  console.log('Auctions status:', auctionsRes.status, 'total:', auctions.totalCount);

  const roomRes = await fetch('http://127.0.0.1:5084/api/v1/auctions/' + auctions.items[0].id + '/room', {
    headers: { Cookie: cookies }
  });
  const room = await roomRes.json();
  console.log('Room state:', room.state);
  console.log('Room bids:', room.bids.map(b => ({ bidder: b.bidder, isMine: b.isMine, amount: b.amount })));
  console.log('Room leader:', room.leader);
  console.log('Room result:', room.result);
}

check().catch(console.error);
