import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
// Run after editing CSS/JS used by authenticated pages. No bundle/dependency required.
const pages=['admin/accounts','admin/analytics-dashboard','admin/category-management','admin/lot-inspection','admin/auction-preparation','admin/dispute-arbitration','farmer/lots','business/market-explorer','business/deposit-wallet','business/auction-room','business/dispute-claim','profile','transactions','notifications','login','access-denied'];
let changed=0;
for(const page of pages){const file='pages/'+page+'.html',before=fs.readFileSync(file,'utf8');
 const after=before.replace(/\b(src|href)="([^"?#]*\/assets\/(?:js|css)\/[^"?#]+\.(?:js|css))(?:\?[^"#]*)?"/g,(match,attr,url)=>{
  const asset=path.resolve(path.dirname(file),url.startsWith('/')?'.'+url:url);
  // Absolute website URLs are rooted at the repository, relative ones at the HTML.
  const resolved=url.startsWith('/')?path.resolve('.'+url):asset;
  const version=crypto.createHash('sha256').update(fs.readFileSync(resolved)).digest('hex').slice(0,12);
  return `${attr}="${url}?v=${version}"`;
 });
 if(after!==before){fs.writeFileSync(file,after);changed++;}
}
console.log(JSON.stringify({pages:pages.length,updated:changed}));
