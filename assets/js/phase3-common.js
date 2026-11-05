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

  const node=(tag,text,className)=>{const el=document.createElement(tag);if(text!==undefined)el.textContent=text;if(className)el.className=className;return el;};
  
  const labels={
    Draft:'Bản nháp',
    Published:'Đã công bố',
    OpenForRegistration:'Đang nhận đăng ký',
    RegistrationClosed:'Hết hạn đăng ký',
    Active:'Đang diễn biến',
    Completed:'Đã kết thúc',
    Cancelled:'Đã hủy',
    Registered:'Đã gửi đăng ký',
    SimulationEligible:'Đã xác nhận cọc',
    Rejected:'Bị từ chối',
    PendingVerification:'Chờ xác nhận cọc',
    Confirmed:'Đã xác nhận cọc',
    Forfeited:'Tịch thu cọc',
    Refunded:'Đã hoàn cọc',
    PendingDeposit:'Chờ xác nhận cọc',
    SimulatedHeld:'Đã giữ cọc',
    AwaitingSimulation:'Chờ xác nhận cọc',
    SimulatedRefunded:'Đã hoàn cọc',
    SimulatedForfeited:'Tịch thu cọc'
  };

  function status(state){
    const s=node('span',labels[state]||Phase2.label(state),'p2-state');
    s.dataset.state = state;
    return s;
  }

  function scheduleStatus(auctionOrTerms) {
    const t = auctionOrTerms ? (auctionOrTerms.terms || auctionOrTerms) : null;
    if(!t) return { code:'Upcoming', text:'Chưa mở đăng ký', badge:'p2-state-muted' };
    const now=Date.now(), opens=new Date(t.registrationOpensAt).getTime(), closes=new Date(t.registrationClosesAt).getTime(), start=new Date(t.startsAt||t.startAt).getTime(), end=new Date(t.endsAt||t.endAt).getTime();
    if(now < opens) return { code:'Upcoming', text:'Chưa mở đăng ký', badge:'p2-state-muted' };
    if(now >= opens && now < closes) return { code:'Registering', text:'Đang nhận đăng ký', badge:'p2-state-warning' };
    if(now >= closes && now < start) return { code:'RegClosed', text:'Hết hạn đăng ký', badge:'p2-state-expired' };
    if(now >= start && now < end) return { code:'Bidding', text:'Đang đấu giá', badge:'p2-state-warning' };
    return { code:'Ended', text:'Đã kết thúc', badge:'p2-state-ended' };
  }

  function primaryStatus(auction) {
    if(!auction) return { text:'—', badge:'p2-state-muted', desc:'', state:'Draft', canPublish:false, isEnded:false };
    if(auction.state==='Draft') return { text:'Bản nháp', badge:'p2-state-muted', desc:'Chưa công bố', state:'Draft', canPublish:true, isEnded:false };
    if(auction.state==='Cancelled') return { text:'Đã hủy', badge:'p2-state-danger', desc:'Phiên đã bị hủy', state:'Cancelled', canPublish:false, isEnded:true };
    if(auction.state==='Completed') return { text:'Đã kết thúc', badge:'p2-state-ended', desc:'Phiên đã đóng', state:'Completed', canPublish:false, isEnded:true };
    const sched = scheduleStatus(auction.terms || auction);
    if(sched.code === 'Upcoming') return { text:'Chưa mở đăng ký', badge:'p2-state-muted', desc:'Chờ mở nhận cọc', state:'Scheduled', canPublish:false, isEnded:false };
    if(sched.code === 'Registering') return { text:'Đang nhận đăng ký', badge:'p2-state-warning', desc:'Nhận cọc mô phỏng', state:'Scheduled', canPublish:false, isEnded:false };
    if(sched.code === 'RegClosed') return { text:'Hết hạn đăng ký', badge:'p2-state-expired', desc:'Chờ đến giờ đấu giá', state:'Scheduled', canPublish:false, isEnded:false };
    if(sched.code === 'Bidding') return { text:'Đang đấu giá', badge:'p2-state-warning', desc:'Phiên đang diễn ra', state:'Active', canPublish:false, isEnded:false };
    return { text:'Đã kết thúc', badge:'p2-state-ended', desc:'Hết giờ đấu giá', state:'Completed', canPublish:false, isEnded:true };
  }

  function button(label,onClick,className='p2-button'){const b=node('button',label,className);b.type='button';if(onClick)b.addEventListener('click',onClick);return b;}

  function amount(value,currency='VND'){
    if(value===undefined||value===null||value===''||isNaN(Number(value))) return '—';
    return Phase2.decimal(value)+' '+(currency||'VND');
  }

  function timeRange(start, end) {
    const wrap = node('div', undefined, 'p3-time-range');
    const row1 = node('div', undefined, 'p3-time-row');
    row1.append(node('span', 'Từ', 'p3-time-label'), node('span', Phase2.time(start), 'p3-time-val'));
    const row2 = node('div', undefined, 'p3-time-row');
    row2.append(node('span', 'Đến', 'p3-time-label'), node('span', Phase2.time(end), 'p3-time-val'));
    wrap.append(row1, row2);
    return wrap;
  }

  function terms(auctionOrTerms){
    if (!auctionOrTerms) return node('div');
    const a = auctionOrTerms;
    const t = a.terms || a;
    const dl=node('dl',undefined,'p3-terms');

    const regOpen = t.registrationOpensAt || a.registrationOpensAt;
    const regClose = t.registrationClosesAt || a.registrationClosesAt;
    const start = t.startsAt || t.startAt || a.startsAt || a.startAt;
    const end = t.endsAt || t.endAt || a.endsAt || a.endAt;
    const depVal = t.depositAmount ?? t.deposit?.amount ?? a.depositAmount ?? a.simulatedDepositAmount;
    const curVal = t.currency || t.deposit?.currency || a.currency || a.content?.currency || 'VND';

    const items=[
      ['Mở đăng ký',Phase2.time(regOpen)],
      ['Đóng đăng ký',Phase2.time(regClose)],
      ['Bắt đầu đấu giá',Phase2.time(start)],
      ['Kết thúc dự kiến',Phase2.time(end)],
      ['Số tiền cọc mô phỏng',amount(depVal, curVal)]
    ];

    const step = t.minimumBidStep ?? t.minimumIncrement ?? a.content?.proposedIncrement;
    if(step !== undefined && step !== null && !isNaN(Number(step))) {
      items.push(['Bước giá đề xuất / tối thiểu', amount(step, curVal)]);
    }

    for(const [k,v] of items){const dt=node('dt',k);const dd=node('dd',v);dl.append(dt,dd);}
    return dl;
  }

  function pager(data,previous,next,label){
    if(typeof previous==='function'){
      const onPage=previous;
      const nav=node('div',undefined,'p3-pager');
      const prev=button('Trước',()=>onPage(data.page-1),'p2-secondary');
      prev.disabled=data.page<=1;
      const nxt=button('Tiếp',()=>onPage(data.page+1),'p2-secondary');
      nxt.disabled=data.page*data.pageSize>=data.totalCount;
      const info=node('span',`Trang ${data.page} / ${Math.max(1,Math.ceil((data.totalCount||data.total)/data.pageSize))} · ${data.totalCount||data.total} kết quả`);
      nav.append(prev,info,nxt);
      return nav;
    }
    if(previous) previous.disabled=data.page<=1;
    if(next) next.disabled=data.page*data.pageSize>=(data.total||data.totalCount);
    if(label) label.textContent=`Trang ${data.page} / ${Math.max(1,Math.ceil((data.total||data.totalCount)/data.pageSize))} · ${data.total||data.totalCount} kết quả`;
  }

  function depositStatus(state) {
    const s = node('span', labels[state] || Phase2.label(state), 'p2-state');
    s.dataset.state = state;
    return s;
  }

  function registrationDetail(r) {
    const root = document.getElementById('registration-detail');
    if (!root) return;
    while (root.firstChild) root.removeChild(root.firstChild);
    root.append(
      node('h3', r.auctionTitle || 'Hồ sơ đăng ký'),
      node('p', `Doanh nghiệp: ${r.enterpriseName}`, 'p3-detail-meta'),
      Phase2.reference(r.id),
      status(r.state),
      node('p', `Mức cọc: ${amount(r.deposit?.amount, r.deposit?.currency)} · Trạng thái: ${labels[r.deposit?.state] || Phase2.label(r.deposit?.state)}`),
      node('p', `${labels[r.deposit?.state] || Phase2.label(r.deposit?.state || 'AwaitingSimulation')}. Khoản cọc được xử lý trong môi trường mô phỏng.`, 'p2-muted')
    );
    if (r.deposit?.confirmedAt) root.append(node('p', `Quản trị viên xác nhận cọc lúc ${Phase2.time(r.deposit.confirmedAt)}. `, 'p3-detail-meta'), Phase2.reference(r.deposit.confirmedBy), Phase2.reference(r.deposit.reference));
    if (r.deposit?.settlementKind) root.append(node('p', `${r.deposit.settlementKind === 'SimulatedRefunded' ? 'Đã hoàn cọc' : 'Tịch thu cọc'} lúc ${Phase2.time(r.deposit.settledAt)}`, 'p2-muted'));
    if (r.history && r.history.length) {
      const list = node('ol');
      for (const event of r.history) { const item=node('li', `${labels[event.state] || Phase2.label(event.state)} · ${Phase2.time(event.occurredAt)} · Người thực hiện `,'p3-detail-meta');item.append(Phase2.reference(event.actorId));list.append(item); }
      root.append(list);
    }
    const dialog = document.getElementById('registration-dialog');
    if (dialog && typeof dialog.showModal === 'function') dialog.showModal();
  }

  function registrationRows(data, adminOrRoot, confirmOrOptions, auctionsMap) {
    let admin = false;
    let confirm = null;
    let root = null;

    if (adminOrRoot && typeof adminOrRoot === 'object' && adminOrRoot.nodeType) {
      root = adminOrRoot;
      if (confirmOrOptions && typeof confirmOrOptions === 'object') {
        admin = !!confirmOrOptions.admin;
        confirm = confirmOrOptions.onConfirm;
      }
    } else {
      admin = Boolean(adminOrRoot);
      confirm = typeof confirmOrOptions === 'function' ? confirmOrOptions : null;
      root = document.getElementById('registration-rows');
    }

    if (!root) return;
    const rows = document.createDocumentFragment();

    for (const r of data.items) {
      const tr = node('tr');
      tr.dataset.registrationId = r.id;

      // 1. Tiêu đề phiên đấu giá
      const tdTitle = node('td');
      const titleStrong = node('strong', r.auctionTitle || 'Phiên đấu giá');
      titleStrong.style.fontWeight = '500';
      tdTitle.append(titleStrong);
      tr.append(tdTitle);

      // 2. Doanh nghiệp (khi admin)
      if (admin) {
        const tdWho = node('td');
        const whoStrong = node('strong', r.enterpriseName || 'Doanh nghiệp');
        whoStrong.style.fontWeight = '500';
        tdWho.append(whoStrong);
        tr.append(tdWho);
      }

      // 3. Thời điểm đăng ký (chỉ hiển thị ngày giờ dạng tabular-nums, gọn gàng trên 1 dòng)
      const tdRegistered = node('td');
      const timeSpan = node('span', Phase2.time(r.registeredAt));
      timeSpan.style.fontVariantNumeric = 'tabular-nums';
      timeSpan.style.whiteSpace = 'nowrap';
      tdRegistered.append(timeSpan);
      tr.append(tdRegistered);

      // 4. Mức cọc (căn phải, tabular-nums)
      const tdDeposit = node('td', amount(r.deposit?.amount, r.deposit?.currency));
      tdDeposit.className = 'p3-col-num';
      tr.append(tdDeposit);

      // 5. Trạng thái cọc (chỉ 1 badge duy nhất mang ý nghĩa nghiệp vụ rõ ràng)
      const tdState = node('td');
      let isExpired = false;
      if (admin && r.state === 'PendingDeposit' && auctionsMap && r.auctionId) {
        const auc = auctionsMap.get(r.auctionId);
        if (auc) {
          const sched = scheduleStatus(auc.terms || auc);
          isExpired = sched.code === 'RegClosed' || sched.code === 'Ended';
        }
      }

      if (r.deposit?.settlementKind === 'SimulatedRefunded') {
        const b = node('span', 'Đã hoàn cọc', 'p2-state');
        b.dataset.state = 'Refunded';
        tdState.append(b);
      } else if (r.deposit?.settlementKind === 'SimulatedForfeited') {
        const b = node('span', 'Tịch thu cọc', 'p2-state');
        b.dataset.state = 'Forfeited';
        tdState.append(b);
      } else if (r.state === 'SimulationEligible' || r.state === 'Confirmed') {
        const b = node('span', 'Đã xác nhận cọc', 'p2-state');
        b.dataset.state = 'Confirmed';
        tdState.append(b);
      } else if (r.state === 'PendingDeposit') {
        if (isExpired) {
          const b = node('span', 'Hết hạn', 'p2-state');
          b.dataset.state = 'RegistrationClosed';
          tdState.append(b);
        } else {
          const b = node('span', 'Chờ xác nhận cọc', 'p2-state');
          b.dataset.state = 'PendingDeposit';
          tdState.append(b);
        }
      } else if (r.state === 'Rejected') {
        const b = node('span', 'Bị từ chối', 'p2-state');
        b.dataset.state = 'Rejected';
        tdState.append(b);
      } else {
        tdState.append(status(r.state));
      }
      tr.append(tdState);

      // 6. Thao tác (chỉ chứa các nút hành động, không chèn chữ Hết hạn vào cột này)
      const tdActions = node('td');
      tdActions.className = 'p3-col-actions';
      const actionGroup = node('div', undefined, 'p2-actions');
      actionGroup.style.margin = '0';
      actionGroup.style.gap = '6px';
      actionGroup.style.justifyContent = 'flex-end';

      actionGroup.append(button('Hồ sơ', () => registrationDetail(r), 'p2-secondary'));

      if (admin && r.state === 'PendingDeposit' && !isExpired && confirm) {
        actionGroup.append(button('Xác nhận cọc', () => confirm(r), 'p2-button'));
      }

      if (!admin && r.state === 'SimulationEligible') {
        const isSettled = r.deposit?.settlementKind === 'SimulatedRefunded' || r.deposit?.settlementKind === 'SimulatedForfeited';
        const link = node('a', isSettled ? 'Xem phòng' : 'Vào phòng đấu giá', isSettled ? 'p2-secondary text-xs' : 'p2-button text-xs');
        link.href = '/pages/business/auction-room.html?auctionId=' + r.auctionId;
        actionGroup.append(link);
      }

      tdActions.append(actionGroup);
      tr.append(tdActions);
      rows.append(tr);
    }

    if (typeof root.replaceChildren === 'function') {
      root.replaceChildren(rows);
    } else {
      while (root.firstChild) root.removeChild(root.firstChild);
      root.append(rows);
    }

    const stateEl = document.getElementById('registration-state');
    if (stateEl) {
      stateEl.textContent = data.items.length ? '' : 'Chưa có đăng ký trong danh sách này.';
    }
  }

  function init() {
    // Delegated close-dialog listener for any existing or future close buttons
    document.addEventListener('click', (e) => {
      const closeBtn = e.target.closest('[data-close-dialog]');
      if (closeBtn) {
        e.preventDefault();
        closeBtn.closest('dialog')?.close();
        return;
      }
      if (e.target.tagName === 'DIALOG' && e.target.hasAttribute('open')) {
        const rect = e.target.getBoundingClientRect();
        const isInDialog = (rect.top <= e.clientY && e.clientY <= rect.top + rect.height &&
          rect.left <= e.clientX && e.clientX <= rect.left + rect.width);
        if (!isInDialog) {
          e.target.close();
        }
      }
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  window.Phase3 = { node, status, depositStatus, button, terms, timeRange, pager, amount, registrationRows, registrationDetail, init, labels, scheduleStatus, primaryStatus };
})();
