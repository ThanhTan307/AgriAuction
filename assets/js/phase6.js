(function(){
  'use strict';
  const $=id=>document.getElementById(id),{node,button}=Phase3,admin=document.body.dataset.phase6==='admin';
  const labels={
    Open:'Đang mở',
    UnderReview:'Đang xem xét',
    MediationPending:'Chờ hòa giải',
    DecisionRecorded:'Đã giải quyết',
    Accept:'Chấp nhận khiếu nại',
    Reject:'Bác bỏ khiếu nại',
    Mediate:'Đề nghị hòa giải',
    Created:'Tạo khiếu nại',
    ResponseAdded:'Giải trình bổ sung',
    ReviewStarted:'Bắt đầu xem xét',
    AdminComment:'Nhận xét của Quản trị viên'
  };
  let selected=new URLSearchParams(location.search).get('complaintId'),current=null,page=1,historyPage=1,targetPage=0,pending=null,busy=false,listGeneration=0,detailGeneration=0,user;
  function msg(text){Phase2.message($('page-message'),text);}
  function failure(error){Phase2.error($('page-message'),error);}
  function controls(){
    for(const form of document.querySelectorAll('#create-form,#response-form,#admin-form')){
      const disabled=busy||!!pending||(form.id!=='create-form'&&(!current||current.complaint.state==='DecisionRecorded'));
      const fs=form.querySelector('fieldset');
      if(fs)fs.disabled=disabled;
      form.querySelectorAll('button').forEach(b=>b.disabled=disabled);
      let hint=form.querySelector('[data-control-hint]');if(!hint){hint=node('p',undefined,'p6-meta');hint.dataset.controlHint='';form.append(hint);}
      hint.textContent=busy?'Đang ghi nhận, vui lòng chờ.':pending?'Chưa xác nhận kết quả. Dùng Thử lại cùng yêu cầu để đối chiếu.':form.id!=='create-form'&&!current?'Chọn một hồ sơ để xem các thao tác được phép.':current?.complaint.state==='DecisionRecorded'&&form.id!=='create-form'?'Hồ sơ đã có quyết định giải quyết; không nhận thêm nội dung.':'';
    }
    if(admin&&current&&!busy&&!pending){
      const state=current.complaint.state;
      const reviewBtn=document.querySelector('[data-command="review"]');
      const decisionBtn=document.querySelector('[data-command="decision"]');
      const commentsBtn=document.querySelector('[data-command="comments"]');
      const reviewPanel=$('review-action-panel');
      const outcomeGroup=$('outcome-field-group');
      const decisionFormContainer=$('decision-form-container');
      const decisionSummary=$('decision-summary');

      if(state==='DecisionRecorded'){
        if(decisionFormContainer)decisionFormContainer.hidden=true;
        if(decisionSummary){
          decisionSummary.hidden=false;
          const outcomeText=labels[current.complaint.outcome]||current.complaint.outcome||'Đã ghi nhận';
          const outEl=$('decision-summary-outcome');
          if(outEl)outEl.textContent='Kết quả: '+outcomeText;
        }
      }else{
        if(decisionFormContainer)decisionFormContainer.hidden=false;
        if(decisionSummary)decisionSummary.hidden=true;
        if(reviewBtn)reviewBtn.disabled=state!=='Open';
        if(reviewPanel)reviewPanel.style.display=state==='Open'?'':'none';
        if(decisionBtn)decisionBtn.disabled=state!=='UnderReview';
        if(commentsBtn)commentsBtn.disabled=false;
        if(outcomeGroup){
          outcomeGroup.querySelectorAll('input,select,button').forEach(el=>{el.disabled=state!=='UnderReview';});
          outcomeGroup.style.opacity=state==='UnderReview'?'1':'0.5';
        }
      }
    }
    $('pending-command').hidden=!pending;$('retry-command').disabled=busy;
  }
  function pager(data,prefix){$(prefix+'prev').disabled=data.page<=1;$(prefix+'next').disabled=data.page*data.pageSize>=data.totalCount;$(prefix+'number').textContent=`Trang ${data.page} / ${Math.max(1,Math.ceil(data.totalCount/data.pageSize))} · ${data.totalCount} kết quả`;}
  async function load(){
    const generation=++listGeneration;$('complaint-list').setAttribute('aria-busy','true');$('list-state').textContent='Đang tải khiếu nại…';
    const query=new URLSearchParams(new FormData($('filter-form')));for(const [k,v]of [...query])if(!v)query.delete(k);query.set('page',page);query.set('pageSize',10);
    try{
      const data=await AgriApi.request('/api/v1/complaints?'+query);if(generation!==listGeneration)return;
      const list=$('complaint-list');list.replaceChildren();
      for(const c of data.items){
        const b=button('',()=>{
          detail(c.id);
          $('case-section')?.scrollIntoView({behavior:'smooth'});
        });
        b.dataset.complaintId=c.id;
        b.setAttribute('aria-current',String(c.id===selected));

        const headerRow=node('div',undefined,'p6-list-row');
        headerRow.style.cssText='display:flex;justify-content:space-between;align-items:flex-start;gap:8px;margin-bottom:4px;';
        headerRow.append(node('strong',c.title));

        const badge=node('span','','p3-badge');
        if(c.state==='Open'){
          badge.classList.add('p3-badge-warn');
          badge.textContent='Cần xử lý · Đang mở';
        }else if(c.state==='UnderReview'){
          badge.classList.add('p3-badge-primary');
          badge.textContent='Đang xem xét';
        }else if(c.state==='DecisionRecorded'){
          badge.classList.add('p3-badge-success');
          badge.textContent='Đã giải quyết'+(c.outcome?' · '+(labels[c.outcome]||c.outcome):'');
        }else{
          badge.classList.add('p3-badge-muted');
          badge.textContent=labels[c.state]||Phase2.label(c.state);
        }
        headerRow.append(badge);

        b.append(headerRow);
        b.append(node('p',c.reason));
        b.append(node('p',`${c.scope==='AuctionRecord'?'Phiên đấu giá':'Hồ sơ giao dịch'} · Cập nhật: ${Phase2.time(c.updatedAt)}`,'p6-meta'));
        list.append(b);
      }
      pager(data,'page-');$('list-state').textContent=data.items.length?'':'Chưa có khiếu nại phù hợp với bộ lọc.';
    }catch(error){
      if(generation===listGeneration){$('complaint-list').replaceChildren();$('list-state').textContent='Không tải được danh sách. Bấm Tải lại để thử lại.';failure(error);}
    }finally{
      if(generation===listGeneration)$('complaint-list').setAttribute('aria-busy','false');
    }
  }
  function evidence(root,items){for(const e of items){const a=node('a');a.href=e.url;a.target='_blank';a.rel='noopener';a.title='Mở bằng chứng riêng tư trong tab mới';const img=node('img');img.src=e.url;img.alt='Ảnh bằng chứng trong hồ sơ';img.loading='lazy';img.addEventListener('error',()=>{img.remove();a.textContent='Không tải được ảnh; kiểm tra phiên đăng nhập.';});a.append(img);root.append(a);}}
  function history(data){
    $('case-history').replaceChildren();$('complainant-evidence').replaceChildren();$('respondent-evidence').replaceChildren();
    for(const e of data.items){
      const li=node('li');li.dataset.entryId=e.id;
      li.append(node('h3',`${labels[e.kind]||Phase2.label(e.kind)}${e.outcome?' · '+(labels[e.outcome]||Phase2.label(e.outcome)):''}`),node('p',`${e.actorRole==='System'?'Hệ thống':Phase2.role(e.actorRole)} · ${Phase2.time(e.occurredAt)}`,'p6-meta'),node('p',e.text));
      if(admin&&e.kind==='DecisionRecorded')li.append(node('p','Mã quyết định: ','p6-meta'),Phase2.reference(e.id));
      const photos=node('div',undefined,'p6-gallery');evidence(photos,e.evidence);li.append(photos);$('case-history').append(li);evidence($(e.actorId===current.complaint.complainantId?'complainant-evidence':'respondent-evidence'),e.evidence);
    }
    if(admin&&current&&current.complaint.state==='DecisionRecorded'){
      const dec=data.items.find(e=>e.kind==='DecisionRecorded');
      if(dec&&$('decision-summary-text'))$('decision-summary-text').textContent=dec.text?`Căn cứ giải quyết: "${dec.text}"`:'';
    }
    for(const id of ['complainant-evidence','respondent-evidence'])if(!$(id).children.length)$(id).append(node('p','Chưa có ảnh của bên này trong trang lịch sử hiện tại.','p6-meta'));
    pager(data,'history-');$('history-state').textContent='Nội dung và ảnh trong trang lịch sử hiện tại. Thời điểm do hệ thống ghi nhận.';
  }
  async function historyLoad(){const id=selected,generation=detailGeneration;$('history-state').textContent='Đang tải lịch sử…';try{const data=await AgriApi.request(`/api/v1/complaints/${id}/entries?page=${historyPage}&pageSize=50`);if(id===selected&&generation===detailGeneration)history(data);}catch(error){failure(error);$('history-state').textContent='Không tải được lịch sử; bấm Tải lại.';}}
  async function detail(id){
    const generation=++detailGeneration;current=null;selected=id;controls();$('case-section').hidden=true;msg('Đang tải hồ sơ…');
    try{
      const data=await AgriApi.request('/api/v1/complaints/'+encodeURIComponent(id));if(generation!==detailGeneration)return;
      current=data;historyPage=1;const c=data.complaint,t=data.transaction.transaction,s=data.transaction.snapshot;
      const url=new URL(location.href);url.searchParams.set('complaintId',c.id);window.history.replaceState(null,'',url);
      $('case-title').textContent=c.title;
      $('case-summary').replaceChildren(
        node('p',`${labels[c.state]||Phase2.label(c.state)}${c.outcome?' · '+(labels[c.outcome]||Phase2.label(c.outcome)):''}`),
        node('p',c.reason),
        node('p',c.description),
        node('p',`${c.scope==='AuctionRecord'?'Liên quan phiên đấu giá':'Liên quan hồ sơ giao dịch'} `,'p6-meta'),Phase2.reference(c.id)
      );
      const link=node('a','Xem hồ sơ giao dịch gốc');link.href='/pages/transactions.html?transactionId='+c.transactionId;$('case-summary').append(link);
      if(c.complaintDeadline)$('case-summary').append(node('p','Hạn khiếu nại: '+Phase2.time(c.complaintDeadline)+' · Hạn giải trình: '+Phase2.time(c.responseDueAt)+(c.respondedAt?' · Đã có giải trình của bên còn lại':''),'p6-meta'));
      $('case-snapshot').replaceChildren(node('strong',s.approvedLot.title),node('p',s.categoryName),node('p',`${Phase2.decimal(s.approvedLot.quantity)} ${s.approvedLot.quantityUnit} · ${s.approvedLot.location}`),node('p',`Nông dân: ${s.farmerName}`),node('p',`Doanh nghiệp thắng: ${s.enterpriseName}`),node('p',`Giá thắng: ${Phase3.amount(t.winningPrice,t.currency)} / ${Phase2.priceBasis(t.priceBasis)}`));
      const partyName=id=>id===t.farmerId?`Nông dân · ${s.farmerName}`:`Doanh nghiệp · ${s.enterpriseName}`;
      $('complainant-summary').replaceChildren(node('strong',partyName(c.complainantId)),Phase2.reference(c.complainantId));
      $('respondent-summary').replaceChildren(node('strong',partyName(c.respondentId)),Phase2.reference(c.respondentId));
      history(data.entries);
      $('case-section').hidden=false;
      document.querySelectorAll('[data-complaint-id]').forEach(n=>n.setAttribute('aria-current',String(n.dataset.complaintId===c.id)));
      msg('');
    }catch(error){
      if(generation===detailGeneration){current=null;failure(error);}
    }finally{
      if(generation===detailGeneration)controls();
    }
  }
  async function targets(){
    const next=targetPage+1,data=await AgriApi.request('/api/v1/transactions?page='+next+'&pageSize=100');
    if(!targetPage)$('target-transaction').replaceChildren(new Option('Chọn hồ sơ giao dịch',''));
    for(const t of data.items)if(!t.policyProfile||t.completedAt)$('target-transaction').append(new Option(`${t.title} · ${Phase2.time(t.completedAt||t.createdAt)}`,t.id));
    targetPage=next;$('target-more').hidden=next*data.pageSize>=data.totalCount;
    const requested=new URLSearchParams(location.search).get('transactionId');if(requested&&[...$('target-transaction').options].some(o=>o.value===requested))$('target-transaction').value=requested;
    if(!data.totalCount)$('target-transaction').options[0].textContent='Chưa có hồ sơ sau đấu giá được phân quyền';
  }
  async function send(command){
    busy=true;controls();msg('Đang ghi nhận…');
    try{const receipt=await AgriApi.request(command.path,{method:'POST',body:command.body,headers:{'Idempotency-Key':command.key}});pending=null;command.form.reset();await load();await detail(receipt.complaintId);msg('Đã ghi nhận nội dung. Thông báo sẽ được gửi tới tài khoản liên quan.');}
    catch(error){if(error.status===0||error.status===503){pending=command;failure(error);}else{pending=null;failure(error);if(error.status===409&&selected){await detail(selected);failure(error);}}}
    finally{busy=false;controls();}
  }
  function submit(form,path,body){if(busy||pending)return;send({form,path,body,key:crypto.randomUUID()});}
  if(!admin){
    $('create-form').addEventListener('submit',event=>{event.preventDefault();submit(event.currentTarget,'/api/v1/complaints',new FormData(event.currentTarget));});
    $('response-form').addEventListener('submit',event=>{event.preventDefault();if(!current)return;const data=new FormData(event.currentTarget);data.set('expectedVersion',String(current.complaint.version));submit(event.currentTarget,`/api/v1/complaints/${selected}/responses`,data);});
    $('target-more').addEventListener('click',()=>targets().catch(failure));
  }else{
    const form=$('admin-form');
    function adminCommand(op){
      if(!current||!form.reportValidity())return;
      const data=new FormData(form);
      if(op==='decision'&&(!data.get('outcome')||!data.get('confirmedSimulation'))){msg('Chọn kết quả và xác nhận trước khi ghi quyết định.');return;}
      const body={expectedVersion:String(current.complaint.version),text:data.get('text')};
      if(op==='decision'){body.outcome=data.get('outcome');body.confirmedSimulation=data.get('confirmedSimulation')==='on';}
      submit(form,`/api/v1/complaints/${selected}/${op}`,body);
    }
    form.addEventListener('submit',e=>{e.preventDefault();adminCommand('decision');});
    form.querySelectorAll('button[type=button]').forEach(b=>b.addEventListener('click',()=>adminCommand(b.dataset.command)));
    $('close-case-detail')?.addEventListener('click',()=>{
      $('case-section').hidden=true;
      selected=null;
      current=null;
      document.querySelectorAll('[data-complaint-id]').forEach(n=>n.removeAttribute('aria-current'));
      const url=new URL(location.href);url.searchParams.delete('complaintId');window.history.replaceState(null,'',url);
      controls();
    });
  }
  $('retry-command').addEventListener('click',()=>{if(pending&&!busy)send(pending);});
  $('filter-form').addEventListener('submit',e=>{e.preventDefault();page=1;load();});
  $('page-prev').addEventListener('click',()=>{page--;load();});
  $('page-next').addEventListener('click',()=>{page++;load();});
  $('history-prev').addEventListener('click',()=>{historyPage--;historyLoad();});
  $('history-next').addEventListener('click',()=>{historyPage++;historyLoad();});
  $('page-reload').addEventListener('click',async()=>{await load();if(selected)await detail(selected);});
  (async()=>{try{user=await Phase2.ready(admin?'Administrator':undefined);controls();await load();if(!admin)await targets();if(selected)await detail(selected);}catch(error){failure(error);}})();
})();
