import fs from 'node:fs';
import path from 'node:path';
import {test as base,expect,request} from '@playwright/test';

const directory=process.env.AGRIAUCTION_E2E_RUN_DIR||'TestResults/diagnostic-local';
fs.mkdirSync(directory,{recursive:true});
export function checkpoint(event,data={}){
 const row={at:new Date().toISOString(),pid:process.pid,event,...data};
 fs.appendFileSync(path.join(directory,'checkpoints.jsonl'),JSON.stringify(row)+'\n');
 console.log('[checkpoint] '+JSON.stringify(row));
}
export async function step(name,fn){
 return base.step(name,async()=>{const start=Date.now();checkpoint('step.start',{name});try{const value=await fn();checkpoint('step.end',{name,elapsedMs:Date.now()-start});return value;}catch(error){checkpoint('step.fail',{name,type:error.name,elapsedMs:Date.now()-start});throw error;}});
}
export function observePage(page){
 let apiCount=0,wsOpened=0,wsClosed=0;const pending=new Map(),statuses=new Map();
 page.on('request',r=>{const u=new URL(r.url());if(u.pathname.startsWith('/api/')){pending.set(r,Date.now());apiCount++;}});
 page.on('requestfinished',r=>pending.delete(r));page.on('requestfailed',r=>pending.delete(r));
 page.on('response',r=>{const u=new URL(r.url());if(u.pathname.startsWith('/api/')){const key=r.request().method()+' '+u.pathname;if(statuses.get(key)!==r.status()){statuses.set(key,r.status());checkpoint('api.status',{method:r.request().method(),path:u.pathname,status:r.status()});}}});
 page.on('websocket',ws=>{wsOpened++;checkpoint('websocket.open',{path:new URL(ws.url()).pathname});ws.on('close',()=>{wsClosed++;checkpoint('websocket.close',{open:wsOpened,closed:wsClosed});});});
 // Page error text can include user content; record only its type, inspect sanitized DOM checkpoints separately.
 page.on('pageerror',e=>checkpoint('page.error',{type:e.name}));
 page.on('close',()=>checkpoint('page.closed',{apiCount,pendingRequests:pending.size,wsOpened,wsClosed}));
}
export async function newContext(browser,options){const context=await browser.newContext(options);context.on('page',observePage);checkpoint('context.open',{contexts:browser.contexts().length});return context;}
export async function loginAPI(credentials){
 const context=await request.newContext({baseURL:'http://localhost:5080',timeout:10000});
 try{
  const csrf=await context.get('/api/v1/auth/csrf');expect(csrf.status()).toBe(200);
  const response=await context.post('/api/v1/auth/login?useCookies=true',{data:{email:credentials.email,password:credentials.password},headers:{'X-CSRF-TOKEN':(await csrf.json()).requestToken}});
  checkpoint('api.login',{status:response.status()});expect(response.status()).toBe(200);
  const token=await context.get('/api/v1/auth/csrf');expect(token.status()).toBe(200);
  return await request.newContext({baseURL:'http://localhost:5080',timeout:10000,storageState:await context.storageState(),extraHTTPHeaders:{'X-CSRF-TOKEN':(await token.json()).requestToken}});
 }finally{await context.dispose();}
}
export async function readyForCapture(page){
 await page.evaluate(()=>{for(const image of document.images)if(image.getAttribute('src'))image.loading='eager';window.scrollTo(0,0);});
 try{await page.waitForFunction(()=>document.fonts.status==='loaded'&&[...document.images].filter(i=>i.getAttribute('src')).every(i=>i.complete&&i.naturalWidth>0),null,{timeout:8000});}
 catch(error){checkpoint('capture.not-ready',await page.evaluate(()=>({fonts:document.fonts.status,images:[...document.images].map(i=>({hasSource:!!i.getAttribute('src'),complete:i.complete,width:i.naturalWidth}))})));throw error;}
}
export async function closeContexts(contexts){
 const results=await Promise.allSettled(contexts.filter(Boolean).map(async(context,index)=>{checkpoint('context.close.start',{index,pages:context.pages().length});let timer;try{await Promise.race([context.close({reason:'E2E finally cleanup'}),new Promise((_,reject)=>{timer=setTimeout(()=>reject(new Error('Context cleanup exceeded 10 seconds')),10000);})]);}finally{clearTimeout(timer);}checkpoint('context.close.end',{index});}));
 const failed=results.filter(r=>r.status==='rejected');if(failed.length)throw new AggregateError(failed.map(r=>r.reason),'E2E context cleanup failed');
}
export const test=base.extend({
 lifecycle:[async({browser},use,testInfo)=>{
  checkpoint('test.start',{title:testInfo.title,timeout:testInfo.timeout});
  const timer=setInterval(()=>checkpoint('heartbeat',{contexts:browser.contexts().length,pages:browser.contexts().reduce((n,c)=>n+c.pages().length,0),activeResources:process.getActiveResourcesInfo()}),10000);timer.unref();
  try{await use();}finally{clearInterval(timer);checkpoint('test.cleanup.start',{status:testInfo.status,contexts:browser.contexts().length});await closeContexts(browser.contexts());checkpoint('test.cleanup.end',{contexts:browser.contexts().length});}
 },{auto:true}]
});
export {expect};
