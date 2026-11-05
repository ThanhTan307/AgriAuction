import fs from 'node:fs';import path from 'node:path';
export default class LifecycleReporter{
 constructor(){this.directory=process.env.AGRIAUCTION_E2E_RUN_DIR||'TestResults/diagnostic-local';fs.mkdirSync(this.directory,{recursive:true});}
 record(event,data={}){fs.appendFileSync(path.join(this.directory,'runner.jsonl'),JSON.stringify({at:new Date().toISOString(),event,...data})+'\n');}
 onBegin(config,suite){this.record('runner.begin',{tests:suite.allTests().length,workers:config.workers,globalTimeout:config.globalTimeout});}
 onTestBegin(test){this.record('test.begin',{title:test.title});}
 onTestEnd(test,result){this.record('test.end',{title:test.title,status:result.status,durationMs:result.duration,retry:result.retry});}
 onEnd(result){this.record('runner.end',{status:result.status});fs.writeFileSync(path.join(this.directory,'runner-completed.json'),JSON.stringify({at:new Date().toISOString(),...result},null,2));}
 onExit(){this.record('runner.exit');}
}
