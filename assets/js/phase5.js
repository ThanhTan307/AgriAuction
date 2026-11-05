(function () {
  'use strict';
  const $=id=>document.getElementById(id), {node,button,amount}=Phase3;
  const inbox=document.body.dataset.phase5==='notifications';
  const labels={
    AwaitingPolicy:'Chờ áp dụng chính sách',
    InProgress:'Đang thực hiện',
    Completed:'Đã hoàn tất',
    PolicyLocked:'Đang khóa chính sách',
    AwaitingRequest:'Chờ yêu cầu thanh toán',
    AwaitingConfirmation:'Chờ Admin xác nhận thanh toán',
    SimulatedConfirmed:'Đã xác nhận thanh toán mô phỏng',
    AwaitingDelivery:'Chờ giao hàng',
    Delivered:'Đã giao hàng (chờ nhận)',
    Received:'Đã nhận hàng',
    IssueReported:'Có sự cố giao nhận · chờ Admin',
    RequestPayment:'Yêu cầu thanh toán mô phỏng',
    ConfirmPayment:'Xác nhận thanh toán mô phỏng',
    Deliver:'Xác nhận đã giao hàng',
    Receive:'Xác nhận đã nhận hàng',
    Complete:'Xác nhận hoàn tất giao dịch',
    ReportDeliveryIssue:'Báo sự cố giao nhận',
    CreatedFromSimulationResult:'Khởi tạo hồ sơ giao dịch',
    ResolveDeliveryIssue:'Xử lý sự cố giao nhận · yêu cầu giao nhận lại',
    DisputeOpened:'Mở tranh chấp',
    DisputeDecisionRecorded:'Ghi nhận quyết định tranh chấp',
    SimulatedRefunded:'Đã hoàn cọc mô phỏng',
    SimulatedHeld:'Đang giữ cọc mô phỏng',
    Refund:'Hoàn cọc mô phỏng',
    Forfeit:'Tịch thu cọc mô phỏng',
    RecordViolation:'Ghi nhận vi phạm có căn cứ',
    WinnerRecorded:'Đã ghi nhận người thắng',
    LotSoldSimulation:'Lô hàng đã đấu giá',
    TransactionCreated:'Đã tạo hồ sơ giao dịch',
    AuctionClosedNoWinner:'Phiên kết thúc, không có người thắng',
    DepositSettlementRecorded:'Đã quyết toán cọc'
  };
  const text=value=>labels[value]||Phase2.label(value);
  function depositMessage(value){
    const match=/^(Refund|SimulatedRefunded|Forfeit|SimulatedForfeited):\s*([\d.,]+)\s*VND\.?([\s\S]*)$/i.exec(value||'');
    if(!match)return value;
    const raw=match[2],normalized=raw.includes(',')?raw.replace(/\./g,'').replace(',','.'):raw;
    const amount=Number(normalized);if(!Number.isFinite(amount))return value;
    const refunded=/^(Refund|SimulatedRefunded)$/i.test(match[1]);
    const explanation=match[3].trim();
    const formatted=new Intl.NumberFormat('vi-VN',{maximumFractionDigits:0}).format(amount);
    return `${refunded?'Đã hoàn cọc mô phỏng':'Đã tịch thu cọc mô phỏng'}: ${formatted} VND.${explanation?' '+explanation:''}`;
  }
  let page=1, generation=0, detailGeneration=0, viewerRole, selected=new URLSearchParams(location.search).get('transactionId');
  function pager(data) {
    $('page-prev').disabled=data.page<=1; $('page-next').disabled=data.page*data.pageSize>=data.totalCount;
    $('page-number').textContent=`Trang ${data.page} / ${Math.max(1,Math.ceil(data.totalCount/data.pageSize))} · ${data.totalCount} kết quả`;
  }
  function field(root,label,value) { const wrap=node('div'),dd=node('dd'); if(value instanceof Node)dd.append(value);else dd.textContent=value;wrap.append(node('dt',label),dd);root.append(wrap); }
  async function detail(id,silent=false) {
    const token=++detailGeneration, root=$('transaction-detail');root.setAttribute('aria-busy','true');if(!silent)root.replaceChildren(node('h2','Chi tiết giao dịch'),node('p','Đang tải hồ sơ…'));
    try {
      const {transaction:t,snapshot:s,history,allowedActions}=await AgriApi.request('/api/v1/transactions/'+encodeURIComponent(id)); if(token!==detailGeneration)return;
      selected=t.id; const url=new URL(location.href);url.searchParams.set('transactionId',t.id);window.history.replaceState(null,'',url);
      document.querySelectorAll('[data-transaction-id]').forEach(n=>n.setAttribute('aria-current',String(n.dataset.transactionId===t.id)));

      // 1. Header & Meta Row
      const headerBox = node('div', undefined, 'p5-detail-header');
      const title = node('h2', t.title);
      const metaRow = node('div', undefined, 'p5-detail-meta-row');
      const refWrap = node('div', undefined, 'p5-ref-group');
      refWrap.append(node('span', 'Mã hồ sơ: ', 'p5-meta-label'), Phase2.reference(t.id));
      const stateBadge = node('span', text(t.state), 'p2-state');
      stateBadge.dataset.state = t.state;
      const roomLink = node('a', 'Xem phòng đấu giá →', 'p5-text-link');
      roomLink.href = '/pages/business/auction-room.html?auctionId=' + t.auctionId;
      metaRow.append(refWrap, stateBadge, roomLink);
      headerBox.append(title, metaRow);

      // 2. Highlight Price Banner
      const priceBox = node('div', undefined, 'p5-price-banner');
      const priceLabel = node('span', 'Giá trúng đấu giá mô phỏng', 'p5-price-label');
      const priceVal = node('strong', amount(t.winningPrice, t.currency), 'p5-price-val');
      const priceBasis = node('p', `Cơ sở: ${Phase2.priceBasis(t.priceBasis)} · Giao dịch mô phỏng, không phát sinh tiền thật`, 'p5-price-sub');
      priceBox.append(priceLabel, priceVal, priceBasis);

      // 3. Action Hub (Operational Controls & Complaint)
      const actionBox = node('div', undefined, 'p5-action-card');
      let hasActions = false;
      const actions = node('div', undefined, 'p2-actions transaction-action-group');
      const issueNote = node('textarea'); issueNote.maxLength = 1000; issueNote.id = 'delivery-issue-note';

      if (t.policyProfile && t.state === 'InProgress') {
        const available = [...(allowedActions || [])];
        if (viewerRole !== 'Administrator' && t.deliveryState !== 'IssueReported') {
          const label = node('label', 'Lý do sự cố giao nhận (bắt buộc khi báo sự cố)'); label.htmlFor = issueNote.id; actionBox.append(label, issueNote);
        }
        if (viewerRole === 'Administrator' && t.deliveryState === 'IssueReported') {
          const label = node('label', 'Nội dung xử lý sự cố (hai bên cần xác nhận giao nhận lại)'); label.htmlFor = issueNote.id; actionBox.append(label, issueNote);
        }
        for (const action of available) {
          hasActions = true;
          const key = crypto.randomUUID(); let payload;
          const isPrimary = ['RequestPayment', 'ConfirmPayment', 'Deliver', 'Receive', 'Complete'].includes(action);
          let labelText = text(action);
          if (viewerRole === 'Administrator') {
            if (action === 'ConfirmPayment') labelText = 'Xác nhận thanh toán (Admin đối chiếu)';
            if (action === 'ResolveDeliveryIssue') labelText = 'Xử lý sự cố giao nhận (Admin phê duyệt)';
          }
          const control = button(labelText, async () => {
            const needsNote = ['ReportDeliveryIssue', 'ResolveDeliveryIssue'].includes(action);
            if (needsNote && !issueNote.value.trim()) { Phase2.message($('page-message'), 'Nhập lý do/nội dung xử lý giao nhận.'); return; }
            payload ??= { expectedVersion: String(t.version), note: needsNote ? issueNote.value.trim() : null };
            actions.querySelectorAll('button').forEach(b => b.disabled = true);
            try {
              await AgriApi.request('/api/v1/transactions/' + t.id + '/actions/' + action, { method: 'POST', headers: { 'Idempotency-Key': key }, body: payload });
              await load(true); await detail(t.id);
            } catch (error) { Phase2.error($('page-message'), error); actions.querySelectorAll('button').forEach(b => b.disabled = false); }
          }, isPrimary);
          control.dataset.action = action;
          if (action === 'ReportDeliveryIssue') control.className = 'p2-danger';
          actions.append(control);
        }
      }
      const claim = node('a', 'Khiếu nại mô phỏng', 'p2-secondary');
      claim.href = viewerRole === 'Administrator' ? '/pages/admin/dispute-arbitration.html' : '/pages/business/dispute-claim.html?transactionId=' + t.id;
      actions.append(claim);
      actionBox.append(actions);

      // 4. Fulfillment & Parties Section
      const fulfillSection = node('section', undefined, 'p5-detail-section');
      fulfillSection.append(node('h3', 'Tiến trình thanh toán & giao nhận'));
      const fulfillFields = node('dl', undefined, 'p5-spec-grid');
      for (const [label, val] of [
        ['Thanh toán', text(t.paymentState)],
        ['Giao nhận', text(t.deliveryState)],
        ['Nông dân (Người bán)', s.farmerName],
        ['Doanh nghiệp (Người mua)', s.enterpriseName],
        ['Chốt phiên', Phase2.time(t.closedAt)],
        ['Tạo hồ sơ', Phase2.time(t.createdAt)]
      ]) field(fulfillFields, label, val);
      if (t.completedAt) field(fulfillFields, 'Hoàn tất lúc', Phase2.time(t.completedAt));
      if (t.disputeState) field(fulfillFields, 'Tranh chấp', text(t.disputeState));
      fulfillSection.append(fulfillFields);

      // 5. Approved Lot Specification Section
      const lotSection = node('section', undefined, 'p5-detail-section');
      lotSection.append(node('h3', 'Bản lưu hồ sơ lô đã duyệt'));
      const lotFields = node('dl', undefined, 'p5-spec-grid');
      Phase2.attributes(lotFields, s.approvedLot, s.categoryName);
      lotSection.append(lotFields);

      // 6. History Timeline Section
      const histSection = node('section', undefined, 'p5-detail-section');
      histSection.append(node('h3', 'Lịch sử xử lý hồ sơ'));
      const log = node('ol', undefined, 'p5-timeline');
      for (const h of history) {
        log.append(node('li', `${text(h.kind)} · ${text(h.state)} · ${Phase2.time(h.occurredAt)}${h.note ? ' · ' + h.note : ''}`, 'p5-meta'));
      }
      histSection.append(log);

      root.replaceChildren(headerBox, priceBox, actionBox, fulfillSection, lotSection, histSection);
      root.dataset.transactionId = t.id;
    } catch(error) { if(token===detailGeneration){root.replaceChildren(node('h2','Không tải được hồ sơ'));Phase2.error($('page-message'),error);} }
    finally {if(token===detailGeneration)root.setAttribute('aria-busy','false');}
  }
  async function settlements(){
    if(inbox||viewerRole!=='Administrator')return;
    let data,accounts=[],transactions={items:[]};
    try {
      [data,accounts,transactions]=await Promise.all([
        AgriApi.request('/api/v1/admin/deposit-settlements'),
        AgriApi.request('/api/v1/admin/accounts'),
        AgriApi.request('/api/v1/transactions?page=1&pageSize=100')
      ]);
    } catch(error) { if(![403,404].includes(error.status))Phase2.error($('page-message'),error);return; }
    const accountNames=new Map(accounts.map(a=>[String(a.id),a.displayName||a.name||'']));
    const transactionByAuction=new Map((transactions.items||[]).map(t=>[String(t.auctionId),t]));
    let section=$('deposit-settlements');if(!section){section=node('section',undefined,'p2-card');section.id='deposit-settlements';document.querySelector('main').append(section);}
    section.replaceChildren(node('h2','Xử lý cọc & Quyết toán tham gia'),node('p','Nghĩa vụ cọc của các bên. Quản trị viên đối chiếu và xác nhận hoàn hoặc tịch thu cọc theo quy chế.'));
    if(!data.length){section.append(node('p','Chưa có khoản cọc nào cần quyết toán.','p2-muted'));return;}
    const list=node('div',undefined,'p5-settlement-list');
    for(const d of data){
      const item=node('article',undefined,'p5-settlement-item p5-note');item.dataset.depositId=d.depositId;
      const transaction=transactionByAuction.get(String(d.auctionId));
      const role=d.isWinner?'Người thắng cuộc':'Người tham gia';
      const enterpriseName=d.enterpriseName||accountNames.get(String(d.enterpriseId))||'Chưa xác định doanh nghiệp';
      const eligibleAt=d.eligibleAt||(d.isWinner?transaction?.completedAt:transaction?.closedAt);
      const eligible=d.settlementKind ? '—' : eligibleAt ? Phase2.time(eligibleAt) : 'Chưa đủ điều kiện';
      const status=labels[d.settlementKind]||d.settlementKind||'Chưa xử lý';

      const summary=node('div',undefined,'p5-settlement-summary');
      const info=node('div',undefined,'p5-settlement-info');
      const title=node('h3',d.title,'p5-settlement-title');
      const sub=node('div',undefined,'p5-settlement-enterprise-row');
      const entName=node('span',enterpriseName,'p5-settlement-enterprise');
      const sep=node('span',' · ','p5-settlement-sep');
      const roleTag=node('span',role,'p5-settlement-role');
      sub.append(entName,sep,roleTag);
      info.append(title,sub);

      const metaGroup=node('div',undefined,'p5-settlement-meta-group');
      const topMeta=node('div',undefined,'p5-settlement-top-meta');
      const amt=node('span',amount(d.amount,d.currency),'p5-settlement-amount');
      const stateBadge=node('span',status,'p2-state');
      if(d.settlementKind==='SimulatedRefunded'||d.settlementKind==='Refund'){
        stateBadge.className='p2-state p2-state-success';
        stateBadge.dataset.state='SimulatedRefunded';
      }else if(d.settlementKind==='Forfeit'||d.settlementKind==='RecordViolation'||d.settlementKind==='SimulatedForfeited'){
        stateBadge.className='p2-state p2-state-danger';
        stateBadge.dataset.state='Forfeit';
      }else{
        stateBadge.className='p2-state p2-state-warning';
        stateBadge.dataset.state='PendingDeposit';
      }
      topMeta.append(amt,stateBadge);
      const eligibleDiv=node('span',`Đủ điều kiện hoàn cọc: ${eligible}`,'p5-settlement-eligible');
      metaGroup.append(topMeta,eligibleDiv);

      summary.append(info,metaGroup);
      item.append(summary);

      if(!d.settlementKind&&['ClosedWithWinner','ClosedNoWinner'].includes(d.resultState)){
        const actionPanel=node('div',undefined,'p5-settlement-action-panel');
        const reason=node('textarea'),evidence=node('input'),decision=node('input');reason.maxLength=2000;evidence.maxLength=1000;decision.maxLength=36;
        for(const [name,input,label] of [['reason',reason,'Lý do quyết định vi phạm'],['evidence',evidence,'Tham chiếu chứng cứ'],['decision',decision,'Mã quyết định vi phạm đã lưu']]){input.id='settlement-'+name+'-'+d.depositId;const l=node('label',label);l.htmlFor=input.id;actionPanel.append(l,input);}
        const actionGroup=node('div',undefined,'p2-actions');
        for(const [action,label,isPrim] of [['Refund','Hoàn cọc (Admin xác nhận)',true],['RecordViolation','Lưu quyết định vi phạm',false],['Forfeit','Tịch thu cọc theo quyết định',false]]){
          const key=crypto.randomUUID();let payload;
          const control=button(label,async()=>{
            if(action==='RecordViolation'&&(!reason.value.trim()||!evidence.value.trim())){Phase2.message($('page-message'),'Cần lý do và tham chiếu chứng cứ.');return;}
            if(action==='Forfeit'&&!/^[0-9a-f-]{36}$/i.test(decision.value.trim())){Phase2.message($('page-message'),'Cần mã quyết định đã lưu.');return;}
            control.disabled=true;
            payload??={reason:action==='RecordViolation'?reason.value.trim():null,evidenceReference:action==='RecordViolation'?evidence.value.trim():null,decisionId:action==='Forfeit'?decision.value.trim():null};
            try{const r=await AgriApi.request('/api/v1/admin/deposit-settlements/'+d.depositId+'/'+action,{method:'POST',headers:{'Idempotency-Key':key},body:payload});if(action==='RecordViolation'){decision.value=r.recordId;Phase2.message($('page-message'),'Đã lưu quyết định; chưa xử lý cọc.','success');}else await settlements();}
            catch(error){Phase2.error($('page-message'),error);}finally{control.disabled=false;}
          }, isPrim);
          control.dataset.settlementAction=action;
          if(action==='Refund'&&d.isWinner&&d.tradeState!=='Completed'){control.disabled=true;control.title='Chỉ hoàn cọc người thắng sau khi hoàn tất giao dịch (Completed)';}
          actionGroup.append(control);
        }
        actionPanel.append(actionGroup);
        item.append(actionPanel);
      }
      list.append(item);
    }
    section.append(list);
  }
  function renderTransactions(data) {
    const root=$('transaction-list'),rows=document.createDocumentFragment();
    for(const t of data.items) {
      const row=button('',()=>detail(t.id));row.className='p2-lot-row';row.dataset.transactionId=t.id;row.setAttribute('aria-current',String(t.id===selected));
      row.append(node('strong',t.title),node('span',amount(t.winningPrice,t.currency)),node('p','Trạng thái: '+text(t.state),'p5-meta'));rows.append(row);
    }
    root.replaceChildren(rows);
  }
  function notificationTitle(n) {
    const kinds = { ComplaintCreated:'Khiếu nại mới', ComplaintResponded:'Có giải trình mới', ComplaintStatusChanged:'Khiếu nại đang được xem xét', ComplaintCommented:'Có nhận xét của quản trị viên', ComplaintDecisionRecorded:'Đã ghi quyết định' };
    return kinds[n.kind] || labels[n.kind] || translateKnown(depositMessage(n.title || 'Thông báo'));
  }
  const actionLabels = {
    RequestPayment: 'Yêu cầu thanh toán',
    ConfirmPayment: 'Xác nhận thanh toán',
    Deliver: 'Giao hàng thành công',
    Receive: 'Xác nhận đã nhận hàng',
    Complete: 'Hoàn tất giao dịch',
    ReportDeliveryIssue: 'Báo sự cố giao nhận',
    ResolveDeliveryIssue: 'Xử lý sự cố giao nhận'
  };
  function translateKnown(value) {
    let result = String(value ?? '').replace(/Simulation/gi, 'mô phỏng');
    for (const [code, label] of Object.entries(labels))
      result = result.replace(new RegExp(`\\b${code}\\b`, 'g'), label);
    return result;
  }
  function formatNotification(n) {
    let title = notificationTitle(n);
    let msg = n.message || '';
    const actionMatch = /^(RequestPayment|ConfirmPayment|Deliver|Receive|Complete|ReportDeliveryIssue|ResolveDeliveryIssue):\s*(.+)$/i.exec(msg);
    if (actionMatch) {
      const actionName = actionMatch[1];
      let detail = actionMatch[2].replace(/\.?\s*Chỉ mô phỏng, không phát sinh tiền thật\.?/i, '').trim();
      title = actionLabels[actionName] || actionName;
      msg = translateKnown(detail);
    } else {
      msg = translateKnown(depositMessage(msg));
      msg = msg.replace(/\.?\s*Chỉ mô phỏng, không phát sinh tiền thật\.?/i, '').trim();
    }
    // Clean up technical internal tags like "theo profile D35"
    msg = msg.replace(/;\s*thanh toán và giao nhận mô phỏng theo profile [a-z0-9_-]+/gi, '');
    msg = msg.replace(/theo profile [a-z0-9_-]+/gi, '');
    return { title, message: msg };
  }
  function renderNotifications(data) {
    const root=$('notification-list'),items=document.createDocumentFragment();
    const unreadCount = Number(data.unreadCount) || 0;
    const unreadBadge = $('unread-count');
    if (unreadBadge) {
      if (unreadCount > 0) {
        unreadBadge.textContent = `${unreadCount} chưa đọc`;
        unreadBadge.hidden = false;
        unreadBadge.style.display = 'inline-flex';
      } else {
        unreadBadge.hidden = true;
        unreadBadge.style.display = 'none';
      }
    }
    window.AppNavigation?.updateUnreadCount(unreadCount);
    if(markAllButton) {
      markAllButton.hidden = unreadCount === 0;
      markAllButton.style.display = unreadCount > 0 ? '' : 'none';
      markAllButton.disabled = markAllBusy || unreadCount === 0;
    }
    for(const n of data.items) {
      const formatted = formatNotification(n);
      const isUnread = !n.readAt;
      const item = node('article', undefined, 'p5-inbox-row' + (isUnread ? ' is-unread' : ''));
      item.dataset.notificationId = n.id;
      item.dataset.read = String(!isUnread);

      const main = node('div', undefined, 'p5-inbox-main');
      const titleRow = node('div', undefined, 'p5-inbox-title-row');
      if (isUnread) {
        const dot = node('span', undefined, 'p5-unread-dot');
        dot.setAttribute('aria-hidden', 'true');
        titleRow.append(dot);
      }
      const title = node('h3', formatted.title, 'p5-inbox-title');
      titleRow.append(title);
      main.append(titleRow);

      if (formatted.message) {
        const lot = node('p', formatted.message, 'p5-inbox-lot');
        main.append(lot);
      }

      const aside = node('div', undefined, 'p5-inbox-aside');
      const time = node('time', Phase2.time(n.createdAt), 'p5-inbox-time');
      const actions = node('div', undefined, 'p5-inbox-actions');

      if (n.transactionId) {
        const a = node('a', 'Xem chi tiết', 'p5-inbox-link');
        a.href = '/pages/transactions.html?transactionId=' + encodeURIComponent(n.transactionId);
        actions.append(a);
      } else if (n.complaintId) {
        const a = node('a', 'Xem chi tiết', 'p5-inbox-link');
        a.href = (viewerRole === 'Administrator' ? '/pages/admin/dispute-arbitration.html' : '/pages/business/dispute-claim.html') + '?complaintId=' + encodeURIComponent(n.complaintId);
        actions.append(a);
      }

      if (isUnread) {
        const readBtn = node('button', 'Đánh dấu đã đọc', 'p5-inbox-btn-read');
        readBtn.type = 'button';
        readBtn.addEventListener('click', async () => {
          readBtn.disabled = true;
          try {
            await AgriApi.request('/api/v1/me/notifications/' + encodeURIComponent(n.id) + '/read', { method: 'POST', body: {} });
            await load(true);
          } catch(error) {
            Phase2.error($('page-message'), error);
            readBtn.disabled = false;
          }
        });
        actions.append(readBtn);
      }

      aside.append(time, actions);
      item.append(main, aside);
      items.append(item);
    }
    root.replaceChildren(items);
  }
  async function load(silent=false) {
    const token=++generation,root=$(inbox?'notification-list':'transaction-list');
    root.setAttribute('aria-busy','true'); if(!silent)$('list-state').textContent=inbox?'Đang tải thông báo…':'Đang tải giao dịch…';
    try {
      const data=await AgriApi.request((inbox?'/api/v1/me/notifications':'/api/v1/transactions')+'?page='+page+'&pageSize=20');if(token!==generation)return;
      if(inbox)renderNotifications(data);else renderTransactions(data);pager(data);
      if(!inbox)await settlements();
      $('list-state').textContent=data.items.length?'':(inbox?'Chưa có thông báo trong trang này.':'Chưa có hồ sơ giao dịch trong trang này.');Phase2.message($('page-message'),'');return true;
    } catch(error){if(token===generation){$('list-state').textContent='Không thể tải dữ liệu. Bấm Tải lại để thử lại.';Phase2.error($('page-message'),error);}return false;}
    finally{if(token===generation)root.setAttribute('aria-busy','false');}
  }
  let markAllBusy=false;
  const markAllButton=$('mark-all-read');
  if(markAllButton)markAllButton.addEventListener('click',async()=>{
    if(markAllBusy)return;
    markAllBusy=true;markAllButton.disabled=true;markAllButton.textContent='Đang cập nhật…';
    let unread=[],failed=0,firstError='';
    try{
      let pageNumber=1,totalPages=1;
      do{
        const data=await AgriApi.request(`/api/v1/me/notifications?page=${pageNumber}&pageSize=100`);
        unread.push(...data.items.filter(n=>!n.readAt).map(n=>n.id));
        totalPages=Math.max(1,Math.ceil(data.totalCount/100));pageNumber++;
      }while(pageNumber<=totalPages);
      for(const id of unread){
        try{await AgriApi.request('/api/v1/me/notifications/'+encodeURIComponent(id)+'/read',{method:'POST',body:{}});}
        catch(error){failed++;firstError||=error.message||'Lỗi cập nhật thông báo.';}
      }
      const refreshed=await load(true);if(!refreshed)return;
      if(failed)Phase2.message($('page-message'),`Đã đánh dấu ${unread.length-failed}/${unread.length} thông báo; ${failed} thông báo chưa cập nhật. ${firstError}`);
      else if(unread.length)Phase2.message($('page-message'),`Đã đánh dấu ${unread.length} thông báo là đã đọc.`,'success');
    }catch(error){
      await load(true);Phase2.error($('page-message'),error);
    }finally{
      markAllBusy=false;
      markAllButton.textContent='Đánh dấu tất cả đã đọc';
      const countMatch=$('unread-count')?.textContent?.match(/\d+/);
      const currentUnread=countMatch?Number(countMatch[0]):0;
      markAllButton.hidden=currentUnread===0;
      markAllButton.style.display=currentUnread>0?'':'none';
      markAllButton.disabled=currentUnread===0;
    }
  });
  $('page-reload').addEventListener('click',async()=>{await load();if(!inbox && selected)await detail(selected);});
  $('page-prev').addEventListener('click',()=>{page--;load();});$('page-next').addEventListener('click',()=>{page++;load();});
  (async()=>{
    try{
      viewerRole=(await Phase2.ready()).role;await load();if(!inbox && selected)await detail(selected);
      let refreshing=false;
      const refresh=async()=>{if(document.hidden||refreshing)return;refreshing=true;try{await load(true);const detailRoot=$('transaction-detail');if(!inbox&&selected&&(!detailRoot.contains(document.activeElement)||document.activeElement===document.body))await detail(selected,true);}finally{refreshing=false;}};
      const timer=setInterval(refresh,10000);
      document.addEventListener('visibilitychange',refresh);
      window.addEventListener('focus',refresh);
      let hub;
      try{
        if(window.signalR&&(await AgriApi.request('/api/v1/capabilities')).completion){
          hub=new signalR.HubConnectionBuilder().withUrl('/hubs/notifications').withAutomaticReconnect().build();
          hub.on('InboxChanged',refresh);
          hub.onreconnected(refresh);
          await hub.start();
        }
      }catch{/* Polling keeps transactions available during reconnect */}
      window.addEventListener('pagehide',()=>{clearInterval(timer);hub?.stop();},{once:true});
    }catch(error){Phase2.error($('page-message'),error);}
  })();
})();
