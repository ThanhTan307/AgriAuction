import fs from 'node:fs';
import {test,expect,loginAPI,newContext,closeContexts} from './support/diagnostics.mjs';
const read=f=>JSON.parse(fs.readFileSync(f,'utf8').replace(/^\uFEFF/,'')),demo=read('.local/phase2-demo-credentials.json');
const accounts={Administrator:read('.local/demo-credentials.json'),Farmer:demo.Farmer,Enterprise:demo.Enterprise};
const paths={dashboard:'/pages/admin/analytics-dashboard.html',inspection:'/pages/admin/lot-inspection.html',preparation:'/pages/admin/auction-preparation.html',categories:'/pages/admin/category-management.html',arbitration:'/pages/admin/dispute-arbitration.html',lots:'/pages/farmer/lots.html',market:'/pages/business/market-explorer.html',registrations:'/pages/business/deposit-wallet.html',room:'/pages/business/auction-room.html',transactions:'/pages/transactions.html',notifications:'/pages/notifications.html',complaints:'/pages/business/dispute-claim.html',profile:'/pages/profile.html'};
const keys={Administrator:['dashboard','inspection','preparation','categories','arbitration','transactions','notifications','profile'],Farmer:['lots','transactions','notifications','complaints','profile'],Enterprise:['market','registrations','room','transactions','notifications','complaints','profile']};
const sequences={Administrator:['dashboard','inspection','preparation','categories','dashboard','arbitration','transactions','notifications','profile'],Farmer:['lots','notifications','complaints','transactions','lots','profile'],Enterprise:['market','registrations','notifications','transactions','market','complaints','profile']};
const apis={},states={},users={};const records=[];
const uiUrl=value=>value+(value.includes('?')?'&':'?')+'ui=20261026-shell2';
test.beforeAll(async()=>{fs.mkdirSync('TestResults/navigation/after',{recursive:true});for(const role of Object.keys(accounts)){apis[role]=await loginAPI(accounts[role]);states[role]=await apis[role].storageState();users[role]=await(await apis[role].get('/api/v1/me')).json();}});
test.afterAll(async()=>{const errors=[];for(const api of Object.values(apis)){try{const r=await api.post('/api/v1/auth/logout',{data:{}});expect([204,401]).toContain(r.status());}catch(e){errors.push(e);}finally{await api.dispose();}}fs.writeFileSync('TestResults/navigation/navigation-proof.json',JSON.stringify({at:new Date().toISOString(),records,businessDataWritten:false},null,2));if(errors.length)throw new AggregateError(errors);});
async function shell(page,role,key){
 await expect(page.locator('#app-navigation')).toHaveAttribute('aria-busy','false');
 await expect(page.locator('main [aria-busy="true"]:visible')).toHaveCount(0);
 await expect(page.locator('main [data-kind="error"]:visible')).toHaveCount(0);
 await expect(page.locator('.app-sidebar')).toHaveCount(1);await expect(page.locator('.app-topbar')).toHaveCount(1);
 await expect(page.locator('.app-sidebar')).toHaveAttribute('data-role',role);await expect(page.locator('.app-sidebar')).toHaveCSS('background-color','rgb(15, 23, 42)');
 expect(await page.locator('#app-role-menu>a').evaluateAll(a=>a.map(x=>x.dataset.navKey))).toEqual(keys[role]);
 await expect(page.locator('#app-role-menu [aria-current=page]')).toHaveCount(1);await expect(page.locator('#app-role-menu [aria-current=page]')).toHaveAttribute('data-nav-key',key);
 await expect(page.locator('.app-brand strong')).toHaveText('AgriAuction');await expect(page.locator('.app-account [data-user-role]')).toHaveText(role);await expect(page.locator('.app-account [data-user-name]')).not.toHaveText('Đang tải tài khoản…');
 await expect(page.locator('.app-logout')).toBeVisible();expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
}
async function responsive(page,role,key){
 for(const [name,width,height]of [['desktop',1440,900],['tablet',768,1024],['mobile',390,844]]){
  await page.setViewportSize({width,height});await shell(page,role,key);
  if(width>=1024){await expect(page.locator('.app-sidebar')).toBeInViewport();await expect(page.locator('main')).toHaveCSS('margin-left','240px');}
  else{
   await expect(page.locator('.app-sidebar')).not.toBeInViewport();await expect(page.locator('main')).toHaveCSS('margin-left','0px');
   await page.locator('#app-menu-toggle').click();await expect(page.locator('.app-sidebar')).toBeInViewport();await expect(page.locator('#app-menu-toggle')).toHaveAttribute('aria-expanded','true');
   await expect(page.locator('.app-drawer-close')).toBeFocused();await page.keyboard.press('Shift+Tab');await expect(page.locator('#app-role-menu a').last()).toBeFocused();await page.keyboard.press('Tab');await expect(page.locator('.app-drawer-close')).toBeFocused();
  }
  if((role==='Administrator'&&['dashboard','categories'].includes(key))||(role==='Farmer'&&['lots','complaints'].includes(key))||(role==='Enterprise'&&['market','notifications'].includes(key))){
   const file=`TestResults/navigation/after/${role}-${paths[key].split('/').at(-1).replace('.html','')}-${name}.png`;await page.screenshot({path:file,animations:'disabled'});
  }
  if(width<1024){await page.keyboard.press('Escape');await expect(page.locator('#app-menu-toggle')).toBeFocused();await expect(page.locator('main')).not.toHaveAttribute('inert','');await expect(page.locator('.app-sidebar')).not.toBeInViewport();}
  records.push({role,key,viewport:{width,height},sidebarTheme:'dark',active:key,overflow:false});
 }
 await page.setViewportSize({width:1440,height:900});
}
for(const role of Object.keys(sequences))test(`${role} click navigation stays consistent across all permitted pages and responsive sizes`,async({browser})=>{
 test.setTimeout(120000);const context=await newContext(browser,{storageState:states[role]}),errors=[];
 try{const page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));let first=true;
  for(const key of sequences[role]){if(first){await page.goto(paths[key]);first=false;}else await page.locator(`#app-role-menu [data-nav-key="${key}"]`).click();await expect(page).toHaveURL(url=>url.pathname===paths[key]&&(!url.search||url.searchParams.get('ui')==='20261026-shell2'));await shell(page,role,key);await responsive(page,role,key);}
  await page.locator('#app-menu-toggle').click();await expect(page.locator('main')).toHaveCSS('margin-left','0px');await expect(page.locator('.app-sidebar')).not.toBeInViewport();
  await page.locator('#app-menu-toggle').click();await expect(page.locator('main')).toHaveCSS('margin-left','240px');await expect(page.locator('.app-sidebar')).toBeInViewport();
  await page.reload();await shell(page,role,'profile');
  await page.setViewportSize({width:390,height:844});await page.locator('#app-menu-toggle').click();
  const destination=sequences[role][0];await page.locator(`#app-role-menu [data-nav-key="${destination}"]`).click();await shell(page,role,destination);await expect(page.locator('#app-menu-toggle')).toHaveAttribute('aria-expanded','false');await expect(page.locator('.app-sidebar')).not.toBeInViewport();
  if(role!=='Administrator')expect((await apis[role].get('/api/v1/admin/reports/dashboard?from=2026-10-23&through=2026-10-26&timeZone=Asia/Ho_Chi_Minh')).status()).toBe(403);
  if(role==='Farmer')expect((await apis[role].get('/pages/business/market-explorer.html')).status()).toBe(403);
  if(role==='Enterprise')expect((await apis[role].get('/pages/farmer/lots.html')).status()).toBe(403);
  expect(errors).toEqual([]);
 }finally{await closeContexts([context]);}
});

test('Enterprise room menu preserves a permitted auctionId and safely falls back for missing or denied rooms; logout works',async({browser})=>{
 const api=apis.Enterprise,context=await newContext(browser,{storageState:states.Enterprise}),errors=[];
 try{const page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));await page.goto(paths.market);await shell(page,'Enterprise','market');
  await expect(page.locator('[data-nav-key=room]')).toHaveAttribute('href',uiUrl(paths.registrations+'?chooseRoom=1'));await page.locator('[data-nav-key=room]').click();await expect(page.locator('.app-room-help')).toBeVisible();await shell(page,'Enterprise','registrations');
  const registrations=await(await api.get('/api/v1/me/registrations?pageSize=100')).json();let id;
  for(const r of registrations.items.filter(r=>r.state==='SimulationEligible').slice(0,15)){const response=await api.get('/api/v1/auctions/'+r.auctionId+'/room');if(response.status()===200){id=r.auctionId;break;}}
  expect(id,'Existing isolated demo should have at least one eligible historical room').toBeTruthy();
  await page.goto(paths.room+'?auctionId='+id);await shell(page,'Enterprise','room');await expect(page.locator('[data-nav-key=room]')).toHaveAttribute('href',uiUrl(paths.room+'?auctionId='+id));
  await page.locator('[data-nav-key=notifications]').click();await shell(page,'Enterprise','notifications');await expect(page.locator('[data-nav-key=room]')).toHaveAttribute('href',uiUrl(paths.room+'?auctionId='+id));
  await page.locator('[data-nav-key=room]').click();await expect(page).toHaveURL('http://localhost:5080'+uiUrl(paths.room+'?auctionId='+id));await shell(page,'Enterprise','room');
  await page.evaluate(userId=>sessionStorage.setItem('agri:room:'+userId,'00000000-0000-4000-8000-000000000000'),users.Enterprise.id);
  await page.goto(paths.notifications);await shell(page,'Enterprise','notifications');await expect(page.locator('[data-nav-key=room]')).toHaveAttribute('href',uiUrl(paths.registrations+'?chooseRoom=1'));
  const published=(await(await api.get('/api/v1/auctions?pageSize=100')).json()).items,registered=new Set(registrations.items.map(r=>r.auctionId));let deniedId;
  for(const candidate of published.filter(a=>!registered.has(a.id)).slice(0,15)){if((await api.get('/api/v1/auctions/'+candidate.id+'/room')).status()===403){deniedId=candidate.id;break;}}
  expect(deniedId,'Existing isolated fixtures must include a room scope denied to this Enterprise').toBeTruthy();
  await page.evaluate(({userId,auctionId})=>sessionStorage.setItem('agri:room:'+userId,auctionId),{userId:users.Enterprise.id,auctionId:deniedId});
  await page.goto(paths.notifications);await shell(page,'Enterprise','notifications');await expect(page.locator('[data-nav-key=room]')).toHaveAttribute('href',uiUrl(paths.registrations+'?chooseRoom=1'));
  await page.locator('.app-logout').click();await expect(page).toHaveURL(/\/pages\/login\.html/);expect((await page.request.get('/api/v1/me')).status()).toBe(401);
  expect(await page.evaluate(id=>sessionStorage.getItem('agri:room:'+id),users.Enterprise.id)).toBeNull();expect(errors).toEqual([]);
  records.push({role:'Enterprise',roomContextPreserved:true,missingRoomSafe:true,deniedRoomSafe:true,deniedRoomStatus:403,logoutRevoked:true,auctionId:id});
 }finally{await closeContexts([context]);}
});
