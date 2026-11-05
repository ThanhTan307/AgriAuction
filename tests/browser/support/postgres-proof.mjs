import fs from 'node:fs';import {spawn} from 'node:child_process';import {checkpoint} from './diagnostics.mjs';

// One read-only psql connection per worker. No synchronous child or per-poll process spawn.
export class PostgresProof{
 constructor(){
  const config=JSON.parse(fs.readFileSync('.local/postgres-test/state.json','utf8').replace(/^\uFEFF/,''));
  if(config.port!==55432)throw new Error('Only isolated test PostgreSQL port 55432 is authorized');
  this.queue=Promise.resolve();this.pending=null;this.buffer='';this.count=0;this.closed=false;
  this.child=spawn('C:/Program Files/PostgreSQL/18/bin/psql.exe',['-X','-qAt','-v','ON_ERROR_STOP=1','-h','127.0.0.1','-p','55432','-U',config.username,'-d','agri_phase1_test','-w'],{windowsHide:true,stdio:['pipe','pipe','pipe'],env:{...process.env,PGPASSWORD:config.password,PGCLIENTENCODING:'UTF8',PGCONNECT_TIMEOUT:'5',PGOPTIONS:'-c statement_timeout=5000 -c lock_timeout=2000 -c default_transaction_read_only=on'}});
  checkpoint('postgres.open',{childPid:this.child.pid,port:55432,database:'agri_phase1_test',readOnly:true});
  this.child.stdout.setEncoding('utf8');this.child.stdout.on('data',chunk=>{this.buffer+=chunk;if(this.buffer.length>16777216){this.fail(new Error('PostgreSQL proof result exceeded 8 MiB'));this.child.kill();return;}let i;while((i=this.buffer.indexOf('\n'))>=0){const line=this.buffer.slice(0,i).trim();this.buffer=this.buffer.slice(i+1);if(line&&this.pending){const p=this.pending;this.pending=null;clearTimeout(p.timer);try{if(!/^(?:[0-9a-f]{2})+$/i.test(line))throw new Error();p.resolve(JSON.parse(Buffer.from(line,'hex').toString('utf8')));}catch{p.reject(new Error('Invalid PostgreSQL proof result'));}}}});
  // Never include PostgreSQL diagnostics (which may contain input) in test logs.
  this.child.stderr.on('data',()=>{this.stderrSeen=true;});
  this.child.stdin.on('error',()=>this.fail(new Error('PostgreSQL proof input closed')));
  this.child.on('error',()=>this.fail(new Error('PostgreSQL proof process failed to start')));
  this.exit=new Promise(resolve=>this.child.on('close',(code,signal)=>{this.closed=true;this.fail(new Error('PostgreSQL proof connection closed'));checkpoint('postgres.closed',{code,signal,queries:this.count});resolve({code,signal});}));
 }
 fail(error){if(this.pending){clearTimeout(this.pending.timer);this.pending.reject(error);this.pending=null;}}
 query(sql){
  const operation=this.queue.then(async()=>{
   if(this.closed)throw new Error('PostgreSQL proof connection is closed');
   if(!sql.trim().startsWith('SELECT '))throw new Error('Only SELECT proof queries are allowed');
   const number=++this.count,start=Date.now();checkpoint('postgres.query.start',{number});
   // JSON aggregates can contain physical newlines. Hex frames one scalar result
   // per line without changing JSON numeric/string content or logging its payload.
   const framed="SELECT encode(convert_to(COALESCE(payload::text,'null'),'UTF8'),'hex') FROM ("+sql.replace(/;\s*$/,'')+") AS proof_payload(payload);\n";
   try{return await new Promise((resolve,reject)=>{const timer=setTimeout(()=>{this.fail(new Error('PostgreSQL proof exceeded 8 seconds'));this.child.kill();},8000);this.pending={resolve,reject,timer};this.child.stdin.write(framed,error=>{if(error)this.fail(new Error('PostgreSQL proof input failed'));});});}
   finally{checkpoint('postgres.query.end',{number,elapsedMs:Date.now()-start});}
  });this.queue=operation.catch(()=>{});return operation;
 }
 async close(){
  if(!this.closed)this.child.stdin.end();
  let timer;try{const result=await Promise.race([this.exit,new Promise((_,reject)=>{timer=setTimeout(()=>{this.child.kill();reject(new Error('PostgreSQL proof cleanup exceeded 5 seconds'));},5000);})]);if(result.code!==0)throw new Error('PostgreSQL proof exited unsuccessfully');}
  finally{clearTimeout(timer);}
 }
}
