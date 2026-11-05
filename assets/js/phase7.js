(function(){
 'use strict';

 // Polyfill replaceChildren for full cross-browser compatibility
 if (typeof Element !== 'undefined' && !Element.prototype.replaceChildren) {
   Element.prototype.replaceChildren = function (...nodes) {
     while (this.firstChild) this.removeChild(this.firstChild);
     this.append(...nodes);
   };
 }
 if (typeof DocumentFragment !== 'undefined' && !DocumentFragment.prototype.replaceChildren) {
   DocumentFragment.prototype.replaceChildren = function (...nodes) {
     while (this.firstChild) this.removeChild(this.firstChild);
     this.append(...nodes);
   };
 }

 const $=id=>document.getElementById(id),{node}=Phase3,zone='Asia/Ho_Chi_Minh',dateForm=$('report-filter'),detailForm=$('detail-filter');
 const labels={Draft:'Bản nháp',Scheduled:'Đã công bố',PendingReview:'Chờ duyệt',Approved:'Đã phê duyệt',Rejected:'Bị từ chối',Prepared:'Chờ bắt đầu',Open:'Đang mở',ClosedWithWinner:'Có người thắng',ClosedNoWinner:'Không có người thắng',UnderReview:'Đang xem xét',MediationPending:'Chờ hòa giải',DecisionRecorded:'Đã giải quyết',AwaitingPolicy:'Chờ áp dụng chính sách',PendingDeposit:'Chờ xác nhận cọc',Enabled:'Đang hoạt động',Disabled:'Đã ngừng hoạt động',Administrator:'Quản trị viên',Farmer:'Nông dân',Enterprise:'Doanh nghiệp',LotReview:'Duyệt lô',DepositConfirmation:'Xác nhận cọc',ComplaintReview:'Xem xét khiếu nại',MediationFollowUp:'Theo dõi hòa giải',Lots:'Lô nông sản',Auctions:'Phiên đấu giá',Rooms:'Phòng đấu giá',Results:'Kết quả phiên đấu giá',Transactions:'Hồ sơ giao dịch',Complaints:'Khiếu nại'};
 const states={lots:['Draft','PendingReview','Approved','Rejected'],auctions:['Draft','Scheduled'],rooms:['Prepared','Open','ClosedWithWinner','ClosedNoWinner'],results:['ClosedWithWinner','ClosedNoWinner'],transactions:['AwaitingPolicy'],complaints:['Open','UnderReview','MediationPending','DecisionRecorded'],pending:['LotReview','DepositConfirmation','ComplaintReview','MediationFollowUp']};
 const formatCount=value=>new Intl.NumberFormat('vi-VN').format(BigInt(value)),time=value=>new Intl.DateTimeFormat('vi-VN',{timeZone:zone,dateStyle:'short',timeStyle:'medium'}).format(new Date(value));
 let applied=null,summary=null,loadedPage=null,page=1,summaryGeneration=0,detailGeneration=0,summaryAbort,detailAbort;
 function notice(message){Phase2.message($('page-message'),message);}
 function sum(dimension,state){return summary.metrics.filter(m=>m.dimension===dimension&&(!state||m.state===state)).reduce((a,m)=>a+BigInt(m.count),0n);}
 function rangeQuery(){return new URLSearchParams({...applied,timeZone:zone});}
 function detailQuery(){const p=rangeQuery();p.set('dataset',detailForm.elements.dataset.value);if(detailForm.elements.state.value)p.set('state',detailForm.elements.state.value);p.set('page',page);p.set('pageSize',detailForm.elements.pageSize.value);return p;}
 function stateOptions(){const root=detailForm.elements.state;root.replaceChildren(new Option('Tất cả trạng thái',''));for(const state of states[detailForm.elements.dataset.value])root.append(new Option(labels[state],state));}
 function exact(value){return Phase2.decimal(value);}
 // Localize the protected server export only for the downloaded presentation.
 // Keep the API contract intact and quote/sanitize all cells again after formatting.
 function localizedCsv(text){
  const rows=text.replace(/^\uFEFF/,'').trimEnd().split(/\r?\n/).slice(1);
  const datasets={results:'Kết quả đã chốt',transactions:'Hồ sơ sau đấu giá',pending:'Hồ sơ chờ xử lý',lots:'Lô nông sản',auctions:'Phiên đấu giá',rooms:'Phòng đấu giá',complaints:'Khiếu nại'};
  const cell=value=>{let s=String(value??'').replace(/[\u0000-\u001f\u007f-\u009f]/g,' ');if(/^[\s\uFEFF]*[=+\-@]/.test(s))s="'"+s;return '"'+s.replaceAll('"','""')+'"';};
  const header=['Từ ngày','Đến hết ngày','Múi giờ','Xuất lúc (giờ Việt Nam)','Chế độ','Nhóm dữ liệu','Mã hồ sơ','Loại','Tiêu đề','Trạng thái','Thời điểm ghi nhận (giờ Việt Nam)','Giá ghi nhận (chưa phải tiền đã thu)','Mã tiền','Cơ sở tính giá'];
  return '\uFEFF'+[header.map(cell).join(','),...rows.map(line=>{
   // The server quotes every cell and flattens embedded controls/newlines.
   const fields=[...line.matchAll(/"((?:[^"]|"")*)"(?:,|$)/g)].map(m=>m[1].replaceAll('""','"'));
   if(fields.length!==header.length)throw new Error('Không đọc được bảng xuất. Vui lòng tải lại báo cáo và thử lại.');
   fields[2]='Giờ Việt Nam (UTC+7)';fields[3]=time(fields[3]);fields[4]=fields[4]==='Simulation'?'Mô phỏng':fields[4];fields[5]=datasets[fields[5]]||fields[5];fields[7]=labels[fields[7]]||Phase2.label(fields[7]);fields[9]=labels[fields[9]]||Phase2.label(fields[9]);fields[10]=time(fields[10]);return fields.map(cell).join(',');
  })].join('\r\n')+'\r\n';
 }
 function card(key,title,value,note){const a=node('article',undefined,'p7-kpi');a.dataset.kpi=key;a.append(node('h2',title),node('strong',String(value)),node('small',note));return a;}
 function metrics(){
  const root=$('kpi-grid');root.replaceChildren();
  const totalClosed = summary.winningAuctions + summary.noWinnerAuctions;
  const winnerRateNote = totalClosed > 0 ? `${summary.winningAuctions}/${totalClosed} phiên đã chốt kết quả` : 'Chưa có phiên chốt';
  const primaryData=[
    ['pending','Hồ sơ chờ xử lý',formatCount(sum('Pending')),'Lô, cọc & khiếu nại chờ Admin'],
    ['pending-lots','Lô chờ kiểm duyệt',formatCount(sum('Lots','PendingReview')),'Lô nông sản mới gửi duyệt'],
    ['auctions','Phiên đấu giá',formatCount(sum('Auctions')),'Chuẩn bị & đã công bố'],
    ['winning','Phiên có người thắng',formatCount(summary.winningAuctions),'Chốt kết quả thành công'],
    ['winner-rate','Tỷ lệ có người thắng',summary.winnerRatePercent===null?'—':exact(summary.winnerRatePercent)+'%',winnerRateNote],
    ['complaints','Khiếu nại phát sinh',formatCount(sum('Complaints')),'Cần xem xét & giải quyết']
  ];
  for(const args of primaryData)root.append(card(...args));

  const secondaryRoot=$('secondary-kpi-grid');
  if(secondaryRoot){
    secondaryRoot.replaceChildren();
    const secondaryData=[
      ['accounts','Tài khoản mới',formatCount(sum('Accounts')),'Thành viên tạo trong kỳ'],
      ['approved-lots','Lô đã duyệt',formatCount(sum('Lots','Approved')),'Đủ điều kiện tạo phiên'],
      ['rejected-lots','Lô bị từ chối',formatCount(sum('Lots','Rejected')),'Không đạt kiểm duyệt'],
      ['no-winner','Phiên không người thắng',formatCount(summary.noWinnerAuctions),'Không có người trả giá'],
      ['bids','Lượt trả giá hợp lệ',formatCount(sum('Bids')),'Lượt đấu giá được chấp nhận'],
      ['transactions','Hồ sơ sau đấu giá',formatCount(sum('Transactions')),'Chuyển quy trình thực hiện']
    ];
    for(const args of secondaryData)secondaryRoot.append(card(...args));
  }

  const pairs=[...new Map(summary.values.map(v=>[JSON.stringify([v.currency,v.priceBasis]),[v.currency,v.priceBasis]])).entries()];const selected=$('value-group').value;$('value-group').replaceChildren();for(const [key,[currency,basis]]of pairs)$('value-group').append(new Option(currency+' / '+Phase2.priceBasis(basis),key));
  if(pairs.some(([key])=>key===selected))$('value-group').value=selected;$('value-group').disabled=!pairs.length;if(!pairs.length)$('value-group').append(new Option('Không có giá ghi nhận trong kỳ',''));renderValues();breakdown();
 }
 function selectedGroup(source){if(!summary||!$('value-group').value)return null;const [currency,basis]=JSON.parse($('value-group').value);return summary.values.find(v=>v.source===source&&v.currency===currency&&v.priceBasis===basis)||null;}
 function scaled(value){const [integer,fraction='']=String(value).split('.');return BigInt(integer)*10000n+BigInt(fraction.padEnd(4,'0'));}
 function renderValues(){
  for(const [source,id]of [['Winning','winning-value'],['Tracked','tracked-value']]){const group=selectedGroup(source);$(id).textContent=group?`${exact(group.total)} ${group.currency} / ${Phase2.priceBasis(group.priceBasis)}`:'Chưa có bản ghi thuộc nhóm trong kỳ';}
  $('actual-revenue').textContent='Chưa khả dụng';const group=selectedGroup($('value-source').value);$('value-chart').replaceChildren();$('chart-rows').replaceChildren();
  if(!group){$('chart-state').textContent='Không có dữ liệu giá cho nguồn và nhóm đang chọn.';return;}
  $('chart-state').textContent=`${formatCount(group.records)} bản ghi · Tổng ${exact(group.total)} ${group.currency} / ${Phase2.priceBasis(group.priceBasis)} · Mô phỏng`;
  const values=group.days.map(d=>scaled(d.total)),max=values.reduce((a,b)=>a>b?a:b,0n),width=Math.max(640,group.days.length*22),ns='http://www.w3.org/2000/svg';
  const svg=document.createElementNS(ns,'svg');svg.setAttribute('viewBox',`0 0 ${width} 240`);svg.style.width=width+'px';svg.setAttribute('role','img');svg.setAttribute('aria-label',`Giá ghi nhận ${group.currency} / ${Phase2.priceBasis(group.priceBasis)} theo ngày Việt Nam`);
  const base=200,barWidth=(width-40)/group.days.length;
  const axisLine=document.createElementNS(ns,'line');axisLine.setAttribute('x1','10');axisLine.setAttribute('y1',String(base));axisLine.setAttribute('x2',String(width-10));axisLine.setAttribute('y2',String(base));axisLine.setAttribute('stroke','#cbd5e1');axisLine.setAttribute('stroke-width','1');svg.append(axisLine);
  group.days.forEach((day,i)=>{
   const height=max===0n?0:Number(values[i]*17000n/max)/100,rect=document.createElementNS(ns,'rect');rect.setAttribute('x',String(20+i*barWidth));rect.setAttribute('y',String(base-height));rect.setAttribute('width',String(Math.max(2,barWidth-5)));rect.setAttribute('height',String(height));rect.dataset.date=day.date;rect.dataset.total=day.total;rect.dataset.records=day.records;
   const title=document.createElementNS(ns,'title');title.textContent=`${day.date}: ${exact(day.total)} ${group.currency} / ${Phase2.priceBasis(group.priceBasis)}, ${formatCount(day.records)} bản ghi`;rect.append(title);svg.append(rect);
   if(group.days.length<=14||i%Math.ceil(group.days.length/12)===0){const t=document.createElementNS(ns,'text');t.setAttribute('x',String(20+i*barWidth));t.setAttribute('y','222');t.textContent=day.date.slice(5);svg.append(t);}
   const tr=node('tr');tr.dataset.chartDate=day.date;
   const tdDate=node('td',day.date);
   const tdRecords=node('td',formatCount(day.records));tdRecords.className='p3-col-num';
   const tdTotal=node('td',exact(day.total)+' '+group.currency+' / '+Phase2.priceBasis(group.priceBasis));tdTotal.className='p3-col-num';
   tr.append(tdDate,tdRecords,tdTotal);$('chart-rows').append(tr);
  });
  const chartScroll=$('value-chart');chartScroll.append(svg);
  setTimeout(()=>{
   const lastActive=group.days.findLastIndex(d=>scaled(d.total)>0n);
   if(lastActive>=0){const targetX=20+lastActive*barWidth;chartScroll.scrollLeft=Math.max(0,targetX-chartScroll.clientWidth/2);}
  },40);
 }
 function breakdown(){
  const root=$('state-breakdown');root.replaceChildren();
  for(const [dimension,title]of [['Accounts','Tài khoản theo vai trò / trạng thái'],['Lots','Lô theo trạng thái'],['Auctions','Phiên chuẩn bị / công bố'],['Rooms','Phòng theo ngày cấu hình'],['Complaints','Khiếu nại theo trạng thái'],['Pending','Hồ sơ đang chờ xử lý']]){
   const group=node('details',undefined,'p7-state-group');group.open=dimension==='Accounts';group.append(node('summary',title));const source=summary.metrics.filter(m=>m.dimension===dimension);
   const expected={Lots:states.lots,Auctions:states.auctions,Rooms:states.rooms,Complaints:states.complaints,Pending:states.pending};
   const rows=dimension==='Accounts'?['Administrator','Farmer','Enterprise'].flatMap(role=>['Enabled','Disabled'].map(state=>({role,state,count:source.find(m=>m.role===role&&m.state===state)?.count||'0'}))):(expected[dimension]||[]).map(state=>({state,count:source.find(m=>m.state===state)?.count||'0'}));const total=rows.reduce((a,r)=>a+BigInt(r.count),0n);
   if(total===0n)group.append(node('p','Chưa có dữ liệu trong kỳ.','p2-muted'));
   for(const row of rows){const line=node('div',undefined,'p7-bar-row'),head=node('div',undefined,'p7-bar-label');head.append(node('span',(row.role?labels[row.role]+' · ':'')+(labels[row.state]||Phase2.label(row.state))),node('strong',formatCount(row.count)));const track=node('div',undefined,'p7-bar-track'),bar=node('span');bar.style.width=(total===0n?0:Number(BigInt(row.count)*10000n/total)/100)+'%';track.append(bar);line.append(head,track);group.append(line);}root.append(group);
  }
 }
 async function loadSummary(){
  const token=++summaryGeneration;summaryAbort?.abort();summaryAbort=new AbortController();detailGeneration++;detailAbort?.abort();summary=null;loadedPage=null;$('report-content').hidden=true;$('kpi-grid').replaceChildren();$('value-chart').replaceChildren();$('detail-rows').replaceChildren();$('detail-generated').textContent='';$('page-number').textContent='—';$('detail-state').textContent='Chờ báo cáo theo kỳ được chọn…';$('report-csv').disabled=true;$('report-state').textContent='Đang tải báo cáo…';$('report-generated').textContent='';$('page-prev').disabled=true;$('page-next').disabled=true;
  try{const data=await AgriApi.request('/api/v1/admin/reports/dashboard?'+rangeQuery(),{signal:summaryAbort.signal});if(token!==summaryGeneration)return;summary=data;metrics();$('report-content').hidden=false;$('report-generated').textContent=`Bản đọc ${time(data.generatedAt)} · Giờ Việt Nam (UTC+7)`;const empty=data.metrics.every(m=>BigInt(m.count)===0n);$('report-state').textContent=empty?'Không có dữ liệu trong kỳ đã chọn.':'';notice('');page=1;await loadDetails();}
  catch(error){if(error.name!=='AbortError'&&token===summaryGeneration){$('report-state').textContent='Không tải được báo cáo. Bấm Tải lại báo cáo để thử lại.';$('detail-state').textContent='Chưa có báo cáo hợp lệ để tải bảng kê.';Phase2.error($('page-message'),error);}}
 }
 async function loadDetails(){
  if(!summary)return;const token=++detailGeneration;detailAbort?.abort();detailAbort=new AbortController();loadedPage=null;$('report-csv').disabled=true;$('detail-state').textContent='Đang tải bảng kê…';$('detail-rows').replaceChildren();$('page-prev').disabled=true;$('page-next').disabled=true;
  try{const query=detailQuery(),data=await AgriApi.request('/api/v1/admin/reports/details?'+query,{signal:detailAbort.signal});if(token!==detailGeneration)return;loadedPage={data,query:query.toString()};
   for(const row of data.items){
     const tr=node('tr');tr.dataset.reportRow=row.id;
     const title=node('td',row.title);title.append(Phase2.reference(row.id));
     const tdKind=node('td',labels[row.kind]||Phase2.label(row.kind));
     const tdState=node('td');
     const badge=node('span',labels[row.state]||Phase2.label(row.state),'p2-state');
     badge.dataset.state=row.state;
     tdState.append(badge);
     const tdTime=node('td',time(row.occurredAt));
     const tdAmount=node('td',row.amount===null?'—':exact(row.amount)+' '+row.currency+' / '+Phase2.priceBasis(row.priceBasis));
     tdAmount.className='p3-col-num';
     tr.append(title,tdKind,tdState,tdTime,tdAmount);
     $('detail-rows').append(tr);
   }
   $('detail-state').textContent=data.items.length?'':'Không có bản ghi phù hợp với bộ lọc bảng kê.';$('detail-generated').textContent='Bản đọc bảng kê '+time(data.generatedAt)+' · '+'Giờ Việt Nam (UTC+7)';
   const total=BigInt(data.totalCount),size=BigInt(data.pageSize),pages=(total+size-1n)/size;$('page-number').textContent=`Trang ${data.page} / ${pages||1n} · ${formatCount(total)} bản ghi`;$('page-prev').disabled=page<=1;$('page-next').disabled=BigInt(page)*size>=total;$('report-csv').disabled=false;
  }catch(error){if(error.name!=='AbortError'&&token===detailGeneration){$('detail-state').textContent='Không tải được bảng kê. Bấm Lọc bảng kê để thử lại.';Phase2.error($('page-message'),error);}}
 }
 dateForm.addEventListener('submit',e=>{e.preventDefault();applied={from:dateForm.elements.from.value,through:dateForm.elements.through.value};detailGeneration++;detailAbort?.abort();loadSummary();});
 $('report-reload').addEventListener('click',()=>{if(applied)loadSummary();});$('value-group').addEventListener('change',renderValues);$('value-source').addEventListener('change',renderValues);
 detailForm.elements.dataset.addEventListener('change',stateOptions);detailForm.addEventListener('submit',e=>{e.preventDefault();page=1;loadDetails();});$('page-prev').addEventListener('click',()=>{page--;loadDetails();});$('page-next').addEventListener('click',()=>{page++;loadDetails();});
 $('report-csv').addEventListener('click',async()=>{
  if(!loadedPage)return;const captured=loadedPage;$('report-csv').disabled=true;
  try{const response=await fetch('/api/v1/admin/reports/details.csv?'+captured.query,{credentials:'same-origin',cache:'no-store'});if(!response.ok){const body=await response.json().catch(()=>({title:'Không thể xuất CSV.'}));throw new AgriApi.ApiError(response.status,body);}const blob=new Blob([localizedCsv(await response.text())],{type:'text/csv;charset=utf-8'}),url=URL.createObjectURL(blob),a=node('a');a.href=url;a.download=`bao-cao-mo-phong-${captured.data.range.from}-${captured.data.range.through}-trang-${captured.data.page}.csv`;document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);notice('Đã xuất trang bảng kê hiện tại; dữ liệu được đọc lại tại lúc xuất.');}
  catch(error){Phase2.error($('page-message'),error);}finally{if(loadedPage===captured)$('report-csv').disabled=false;}
 });
 (async()=>{try{await Phase2.ready('Administrator');const params=new URLSearchParams(location.search),today=new Intl.DateTimeFormat('en-CA',{timeZone:zone,year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date()),start=new Date(today+'T00:00:00Z');start.setUTCDate(start.getUTCDate()-29);dateForm.elements.from.value=params.get('from')||start.toISOString().slice(0,10);dateForm.elements.through.value=params.get('through')||today;applied={from:dateForm.elements.from.value,through:dateForm.elements.through.value};stateOptions();await loadSummary();}catch(error){$('report-state').textContent='Không thể mở báo cáo.';Phase2.error($('page-message'),error);}})();
 window.addEventListener('pagehide',()=>{summaryAbort?.abort();detailAbort?.abort();},{once:true});
})();
