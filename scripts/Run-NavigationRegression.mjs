import fs from 'node:fs';import path from 'node:path';import {spawn} from 'node:child_process';
const root='TestResults/navigation';fs.mkdirSync(root,{recursive:true});const file=root+'/regression-progress.json';
const read=p=>JSON.parse(fs.readFileSync(p,'utf8'));
function latest(label){for(const e of fs.readdirSync('TestResults/runner-fix',{withFileTypes:true}).filter(e=>e.isDirectory()&&e.name.startsWith(label+'-')).sort((a,b)=>b.name.localeCompare(a.name))){try{return read(path.join('TestResults/runner-fix',e.name,'suite-summary.json'));}catch{}}return null;}
const from=Number(process.argv.find(a=>a.startsWith('--from='))?.split('=')[1]??1);if(!Number.isInteger(from)||from<1||from>9)throw new Error('Invalid --from=1..9');
const previous=from>1?read(file):null;if(previous&&(!previous.suites.slice(0,from-1).every(s=>s.status==='PASS'&&s.supervisorExit===0)||previous.suites.length<from-1))throw new Error('Cannot resume without earlier verified suites');
if(fs.existsSync(file))fs.copyFileSync(file,file.replace('.json','-previous-'+Date.now()+'.json'));
const progress={started:previous?.started??new Date().toISOString(),resumedAt:previous?new Date().toISOString():null,status:'RUNNING',suites:previous?.suites.slice(0,from-1)??[]};const save=()=>fs.writeFileSync(file,JSON.stringify(progress,null,2));save();let index=0;
for(const [name,count]of [['category',4],['phase2',5],['phase3',4],['phase4',2],['phase5',3],['phase6',2],['phase7',2],['farmer-categories',2],['dialog-layout',2]]){
 if(++index<from)continue;
 const label='nav-regression-'+name,log=fs.openSync(root+'/'+label+'-supervisor.log','w');
 const child=spawn(process.execPath,['scripts/Run-BrowserSuite.mjs',label,'tests/browser/'+name+'.spec.mjs'],{windowsHide:true,stdio:['ignore',log,log]});progress.current={suite:name,pid:child.pid,started:new Date().toISOString()};save();
 const code=await new Promise(resolve=>child.on('close',resolve));fs.closeSync(log);const result=latest(label);progress.suites.push({...result,suite:name,supervisorExit:code,expectedTests:count});progress.current=null;save();console.log(JSON.stringify({suite:name,status:result?.status,passed:result?.stats?.expected,code}));
 if(code!==0||result?.status!=='PASS'||result.stats.expected!==count){progress.status='FAIL';break;}
}
if(progress.suites.length===9&&progress.suites.every(s=>s.status==='PASS'&&s.supervisorExit===0))progress.status='PASS';progress.finished=new Date().toISOString();save();process.exitCode=progress.status==='PASS'?0:1;
