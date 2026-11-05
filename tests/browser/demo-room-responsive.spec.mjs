import fs from 'node:fs';
import {test,expect,loginAPI,newContext,closeContexts} from './support/diagnostics.mjs';
test('Preserved GVHD result remains readable at desktop tablet and mobile without changing business data',async({browser})=>{
 const credentials=JSON.parse(fs.readFileSync('.local/demo-credentials.json','utf8').replace(/^\uFEFF/,'')),api=await loginAPI(credentials);let c;
 try{c=await newContext(browser,{storageState:await api.storageState()});const p=await c.newPage();await p.goto('/pages/business/auction-room.html?auctionId=c541a59e-31f9-4a7e-bd6d-b8199e2f723a&ui=20261026-shell2');await expect(p.locator('#room-workspace')).toBeVisible();
 fs.mkdirSync('TestResults/demo-qa27/room',{recursive:true});
 for(const[name,width,height]of [['desktop',1440,900],['tablet',768,1024],['mobile',390,844]]){await p.setViewportSize({width,height});await expect(p.locator('#current-top-price')).toHaveText('150.000 VND');await expect(p.locator('#bid-history-tbody tr')).toHaveCount(6);await expect(p.locator('.app-sidebar')).toHaveCount(1);expect(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await p.evaluate(()=>document.fonts.ready);await p.screenshot({path:`TestResults/demo-qa27/room/${name}.png`,fullPage:true,animations:'disabled'});
  if(width<1024){await p.locator('#app-menu-toggle').click();await expect(p.locator('.app-sidebar')).toBeInViewport();await expect(p.locator('.app-sidebar')).toHaveCSS('background-color','rgb(15, 23, 42)');await p.keyboard.press('Escape');await expect(p.locator('.app-sidebar')).not.toBeInViewport();}
 }
 }finally{await closeContexts([c]);try{expect((await api.post('/api/v1/auth/logout',{data:{}})).status()).toBe(204);}finally{await api.dispose();}}
});
