import fs from 'node:fs';
import crypto from 'node:crypto';
import {test,expect,loginAPI,newContext,closeContexts} from './support/diagnostics.mjs';
test('Category initializes with current versioned assets even when unversioned scripts are stale',async({browser})=>{
 const account=JSON.parse(fs.readFileSync('.local/demo-credentials.json','utf8').replace(/^\uFEFF/,''));
 const api=await loginAPI(account);let context;
 try{context=await newContext(browser,{storageState:await api.storageState()});const page=await context.newPage(),errors=[],stale=[];
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/assets/js/*.js',async route=>{
  if(!new URL(route.request().url()).searchParams.has('v')){stale.push(route.request().url());await route.fulfill({contentType:'text/javascript',body:"document.getElementById('removed-legacy-header').addEventListener('click',()=>{});"});}
  else await route.continue();
 });
 await page.goto('/pages/admin/category-management.html');
 await expect(page.locator('#app-navigation')).toHaveAttribute('aria-busy','false');
 await expect(page.locator('.app-sidebar')).toBeVisible();await expect(page.locator('#category-rows')).toHaveAttribute('aria-busy','false');
 await expect(page.locator('#category-connection')).toHaveText('Đã cập nhật');
 expect(errors).toEqual([]);expect(stale).toEqual([]);
 const resources=await page.locator('script[src],link[rel=stylesheet]').evaluateAll(nodes=>nodes.map(n=>n.src||n.href));
 for(const url of resources){const u=new URL(url);if(!/^\/assets\/(js|css)\//.test(u.pathname))continue;const expected=crypto.createHash('sha256').update(fs.readFileSync('.'+u.pathname)).digest('hex').slice(0,12);expect(u.searchParams.get('v')).toBe(expected);}
 // Emulate an old cached HTML page at the legacy URL, while allowing the versioned URL through.
 await page.route('**/pages/admin/auction-preparation.html*',async route=>{
  if(!new URL(route.request().url()).searchParams.has('ui'))await route.fulfill({contentType:'text/html',body:'<aside>B2B ADMIN PORTAL — legacy cached HTML</aside>'});
  else await route.continue();
 });
 await page.locator('[data-nav-key=preparation]').click();
 await expect(page).toHaveURL(/auction-preparation\.html\?ui=20261027-content4$/);
 await expect(page.locator('#app-navigation')).toHaveAttribute('aria-busy','false');
 await expect(page.locator('.app-brand strong')).toHaveText('AgriAuction');
 await expect(page.locator('#app-role-menu>a')).toHaveCount(8);expect(errors).toEqual([]);
 }finally{await closeContexts([context]);try{expect((await api.post('/api/v1/auth/logout',{data:{}})).status()).toBe(204);}finally{await api.dispose();}}
});
