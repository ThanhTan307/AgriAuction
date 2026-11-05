import fs from 'node:fs';
import {test,expect,loginAPI,newContext,closeContexts} from './support/diagnostics.mjs';
const read=f=>JSON.parse(fs.readFileSync(f,'utf8').replace(/^\uFEFF/,''));
const demo=read('.local/phase2-demo-credentials.json');
const accounts={Administrator:read('.local/demo-credentials.json'),Farmer:demo.Farmer,Enterprise:demo.Enterprise};
const routes={Administrator:['admin/analytics-dashboard','admin/lot-inspection','admin/auction-preparation','admin/category-management','admin/dispute-arbitration','transactions','notifications','profile'],Farmer:['farmer/lots','transactions','notifications','business/dispute-claim','profile'],Enterprise:['business/market-explorer','business/deposit-wallet','transactions','notifications','business/dispute-claim','profile']};
const records=[];
test.afterAll(()=>{fs.mkdirSync('TestResults/navigation-typography',{recursive:true});fs.writeFileSync('TestResults/navigation-typography/after.json',JSON.stringify({at:new Date().toISOString(),records},null,2));});
for(const role of Object.keys(routes))test(`${role} sidebar typography stays identical after clicking between pages`,async({browser})=>{
 test.setTimeout(120000);const api=await loginAPI(accounts[role]);let context;
 try{context=await newContext(browser,{storageState:await api.storageState()});const page=await context.newPage();await page.goto('/pages/'+routes[role][0]+'.html');
 for(const route of routes[role]){
  if(new URL(page.url()).pathname!=='/pages/'+route+'.html')await page.locator(`.app-menu a:not([data-nav-key="room"])[href^="/pages/${route}.html"]`).click();
  await expect(page.locator('#app-navigation')).toHaveAttribute('aria-busy','false');await page.evaluate(()=>document.fonts.ready);
  for(const width of [1440,768,390]){await page.setViewportSize({width,height:900});
   const metrics=await page.locator('.app-menu a').evaluateAll(links=>links.map(a=>{const label=a.lastElementChild,s=getComputedStyle(label),p=getComputedStyle(a);return {key:a.dataset.navKey,active:a.hasAttribute('aria-current'),font:s.fontFamily,size:s.fontSize,weight:s.fontWeight,lineHeight:s.lineHeight,letterSpacing:s.letterSpacing,smoothing:s.webkitFontSmoothing,textRendering:s.textRendering,linkHeight:a.getBoundingClientRect().height,zoom:p.zoom};}));
   records.push({role,route,width,metrics});
   for(const m of metrics){expect(m.font).toBe('Roboto, sans-serif');expect(m.size).toBe('13px');expect(m.weight).toBe(m.active?'600':'400');expect(m.lineHeight).toBe('18.2px');expect(m.letterSpacing).toBe('normal');expect(m.smoothing).toBe('antialiased');expect(m.zoom).toBe('1');}
  }
  await page.setViewportSize({width:1440,height:900});
 }
 }finally{await closeContexts([context]);try{expect((await api.post('/api/v1/auth/logout',{data:{}})).status()).toBe(204);}finally{await api.dispose();}}
});
