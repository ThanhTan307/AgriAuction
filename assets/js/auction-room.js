(async function () {
  'use strict';
  const $=id=>document.getElementById(id), P=Phase3, notice=$('page-message');
  const auctionId=new URLSearchParams(location.search).get('auctionId');
  let user,snapshot,auction,connection,connected=false,blocked=false,busy=false,fetching=false,queued=false,minimumVersion=0n,lastVersion=0n,mineOnly=false,attempt=null,retryTimer;
  let clockAt=0,clockServer=0,lastServer=0;
  let olderBids=[],olderCursor,olderDone=false,olderButton;
  let transactionEnterpriseName=null;
  const labels={Prepared:'Đã cấu hình · Chưa bắt đầu',Open:'Đang đấu giá mô phỏng',ClosedWithWinner:'Đã chốt · Có người thắng mô phỏng',ClosedNoWinner:'Đã chốt · Không có lượt trả giá'};
  const eventLabels = { Configured:'Đã cấu hình phòng', RoomConfigured:'Đã cấu hình phòng', Started:'Bắt đầu đấu giá', RoomStarted:'Bắt đầu đấu giá', RoomOpened:'Bắt đầu đấu giá', BidAccepted:'Đã ghi nhận lượt trả giá', Closed:'Đã chốt phiên', RoomClosed:'Đã chốt phiên', Extended:'Đã gia hạn phiên' };
  P.init();
  const money=value=>value==null?'Chưa có lượt trả giá':`${Phase2.decimal(value)} ${snapshot?.rules.currency||''}`;
  const serverNow=()=>clockServer+(performance.now()-clockAt);
  function realtime(state,text) { connected=state==='Connected'; $('room-realtime').dataset.state=state; $('room-realtime').textContent=text; tick(); }
  function eligibilityText(next) {
    if(next.state.startsWith('Closed')) return 'Phiên đã kết thúc; không nhận thêm lượt trả giá.';
    if(next.state==='Prepared') return 'Chưa bắt đầu đấu giá. Quản trị viên bắt đầu theo lịch đã công bố.';
    if(user.role!=='Enterprise') return 'Bạn đang theo dõi phiên. Chỉ doanh nghiệp đủ điều kiện được đặt giá.';
    return next.canBid ? 'Đủ điều kiện đặt giá mô phỏng. Không có tiền thật.' : 'Hiện chưa được đặt giá. Kiểm tra xác nhận cọc và thời hạn của phiên.';
  }
  function tick() {
    if(!snapshot) return;
    const now=serverNow(),remaining=Math.max(0,Math.ceil((Date.parse(snapshot.endsAt)-now)/1000));
    const hours=Math.floor(remaining/3600),minutes=Math.floor(remaining%3600/60),seconds=remaining%60;
    const isClosed=snapshot.state.startsWith('Closed');
    const displayEl = $('countdown-display');
    if (displayEl) {
      displayEl.textContent=isClosed?'Đã chốt phiên':snapshot.state==='Prepared'?'Chưa bắt đầu':`${String(hours).padStart(2,'0')}:${String(minutes).padStart(2,'0')}:${String(seconds).padStart(2,'0')}`;
      displayEl.classList.toggle('is-closed', isClosed);
      displayEl.classList.toggle('is-open', snapshot.state==='Open');
      displayEl.classList.toggle('is-prepared', snapshot.state==='Prepared');
    }
    const allowed=!!snapshot.canBid&&connected&&!blocked&&!busy&&remaining>0;
    $('btn-submit-bid').disabled=!allowed; $('bid-input').disabled=!allowed; $('bid-minimum').disabled=!allowed||!!attempt;
    $('room-eligibility').textContent = !connected && snapshot.state==='Open' ? 'Đang chờ kết nối trực tiếp; tạm chưa thể đặt giá.' : busy ? 'Đang chờ hệ thống xác nhận thao tác.' : eligibilityText(snapshot);
    $('room-start').title = now < Date.parse(snapshot.startsAt) ? 'Chưa đến giờ bắt đầu đã công bố.' : now >= Date.parse(snapshot.endsAt) ? 'Đã hết thời gian dự kiến của phiên.' : '';
    $('room-start').disabled=busy||snapshot.state!=='Prepared'||now<Date.parse(snapshot.startsAt)||now>=Date.parse(snapshot.endsAt);
  }
  function ruleList(r) {
    const root=$('room-rules'); root.replaceChildren();
    const names={AtLeastStartingPrice:'Ít nhất bằng giá khởi điểm',MinimumIncrease:'Tăng ít nhất một bước',StartingPriceGrid:'Giá khởi điểm + n × bước giá',FirstCommittedWins:'Giữ lệnh được hệ thống ghi nhận trước',Disabled:'Tắt gia hạn',FromBidTime:'Lấy mốc muộn hơn: hạn cũ hoặc thời điểm trả giá cộng thời gian gia hạn',AddToDeadline:'Hạn cũ + lượng gia hạn',AtOrAfterDeadline:'Hệ thống tự chốt khi đến hoặc quá hạn'};
    for(const [label,value] of [['Giá khởi điểm',money(r.startingPrice)],['Bước giá',money(r.minimumIncrement)],['Cơ sở giá',Phase2.priceBasis(r.priceBasis)],['Lượt trả giá đầu',names[r.firstBidRule]],['Bước',names[r.incrementRule]],['Đồng giá',names[r.tieRule]],['Gia hạn',names[r.extensionRule]],['Khoảng kích hoạt / thời gian thêm / số lần tối đa',`${r.extensionWindowSeconds}s / ${r.extensionSeconds}s / ${r.maxExtensions} lần`],['Chốt',names[r.closeRule]]]) {const n=P.node('div');n.append(P.node('dt',label),P.node('dd',value));root.append(n);}
  }
  async function loadEnterpriseName() {
    try {
      if (user?.role === 'Administrator') {
        const trans = await AgriApi.request('/api/v1/transactions?page=1&pageSize=50');
        const t = trans.items?.find(x => x.auctionId === auctionId);
        if (t) {
          const detail = await AgriApi.request('/api/v1/transactions/' + t.id);
          if (detail.snapshot?.enterpriseName) {
            transactionEnterpriseName = detail.snapshot.enterpriseName;
          }
        }
      }
    } catch { /* best effort */ }
  }

  function getCompetitorLabel(bidderCode) {
    if (!bidderCode) return 'Doanh nghiệp khác';
    const competitorCodes = [];
    const isWinnerIdentified = Boolean(user?.role === 'Administrator' && transactionEnterpriseName && snapshot?.result?.winner);

    if (snapshot?.bids) {
      for (const b of snapshot.bids) {
        const isIdentifiedWinner = isWinnerIdentified && b.bidder === snapshot.result.winner;
        if (!b.isMine && !isIdentifiedWinner && b.bidder && !competitorCodes.includes(b.bidder)) {
          competitorCodes.push(b.bidder);
        }
      }
    }
    if (snapshot?.result?.winner && !isWinnerIdentified && !snapshot.bids?.some(b => b.isMine && b.bidder === snapshot.result.winner)) {
      if (!competitorCodes.includes(snapshot.result.winner)) {
        competitorCodes.push(snapshot.result.winner);
      }
    }
    if (snapshot?.leader && !snapshot.bids?.some(b => b.isMine && b.bidder === snapshot.leader)) {
      if (!isWinnerIdentified || snapshot.leader !== snapshot.result.winner) {
        if (!competitorCodes.includes(snapshot.leader)) {
          competitorCodes.push(snapshot.leader);
        }
      }
    }

    if (competitorCodes.length <= 1) {
      return 'Doanh nghiệp khác';
    }

    let idx = competitorCodes.indexOf(bidderCode);
    if (idx === -1) idx = competitorCodes.length;
    const letter = String.fromCharCode(65 + (idx % 26));
    return `Doanh nghiệp khác ${letter}`;
  }

  function resolveBidder(bidderCode, isMine, isWinner = false) {
    if (!bidderCode) return '—';
    if (isMine) return 'Bạn';
    if (user?.role === 'Administrator' && transactionEnterpriseName && (isWinner || snapshot?.result?.winner === bidderCode)) {
      return transactionEnterpriseName;
    }
    return getCompetitorLabel(bidderCode);
  }
  function renderLotAttributes(content, categoryName) {
    const mainDl = $('lot-attributes');
    if (mainDl) {
      mainDl.replaceChildren();
      for (const [label, val] of [
        ['Loại nông sản', categoryName],
        ['Số lượng', Phase2.decimal(content.quantity) + ' ' + content.quantityUnit],
        ['Khối lượng', Phase2.decimal(content.weight) + ' ' + content.weightUnit],
        ['Địa điểm khai báo', content.location],
        ['Mô tả của nông dân', content.description],
        ['Thông tin chất lượng tự khai báo', content.qualityNotes || 'Chưa cung cấp']
      ]) {
        const wrap = P.node('div'), dt = P.node('dt', label), dd = P.node('dd', val);
        wrap.append(dt, dd); mainDl.append(wrap);
      }
    }
    const propDl = $('farmer-proposal-attributes');
    if (propDl) {
      propDl.replaceChildren();
      for (const [label, val] of [
        ['Giá khởi điểm đề xuất', Phase2.decimal(content.proposedStartingPrice) + ' ' + content.currency],
        ['Bước giá đề xuất', Phase2.decimal(content.proposedIncrement) + ' ' + content.currency],
        ['Cơ sở tính giá đề xuất', Phase2.priceBasis(content.priceBasis)],
        ['Bắt đầu đề xuất', Phase2.time(content.proposedStartsAt)],
        ['Kết thúc đề xuất', Phase2.time(content.proposedEndsAt)]
      ]) {
        const wrap = P.node('div'), dt = P.node('dt', label), dd = P.node('dd', val);
        wrap.append(dt, dd); propDl.append(wrap);
      }
    }
  }
  function bids() {
    const rows=document.createDocumentFragment();const combined=[...new Map([...snapshot.bids,...olderBids].map(b=>[b.id,b])).values()].sort((a,b)=>BigInt(a.version)>BigInt(b.version)?-1:1); const items=combined.filter(b=>!mineOnly||b.isMine);
    for(const b of items) {
      const tr=P.node('tr');
      tr.dataset.bidId=b.id;
      tr.dataset.version=b.version;
      tr.dataset.leading=String(b.version===snapshot.bids[0]?.version);
      const time=P.node('td',Phase2.time(b.acceptedAt));
      time.className = 'col-time';
      const isWinnerBid = snapshot?.result?.winner === b.bidder && (snapshot.result?.winningBidId === b.id || b.version === snapshot.bids[0]?.version);
      const bidderName = resolveBidder(b.bidder, b.isMine, isWinnerBid);
      const who=P.node('td', bidderName);
      who.className = 'col-bidder' + (b.isMine ? ' is-mine' : '');
      const amount=P.node('td');
      amount.className = 'col-price';
      const priceWrap = P.node('div', undefined, 'p4-price-cell-content');
      const priceSpan = P.node('span', money(b.amount), 'price-figure');
      priceWrap.append(priceSpan);
      if(b.extended) {
        const tag = P.node('span','+ gia hạn','p4-ext-badge');
        priceWrap.append(tag);
      }
      amount.append(priceWrap);
      tr.append(time,who,amount);
      rows.append(tr);
    }
    $('bid-history-tbody').replaceChildren(rows);
    $('bid-history-state').textContent=items.length?'':'Chưa có lượt trả giá phù hợp.';
    if(olderButton) {
      olderButton.disabled=olderDone||(!olderBids.length&&snapshot.bids.length<100);
      olderButton.textContent=olderDone?'Đã tải hết lịch sử':olderButton.disabled?'Không còn lịch sử cũ hơn':'Tải lượt giá cũ hơn';
    }
  }
  function render(next) {
    const version=BigInt(next.version),stamp=Date.parse(next.serverTime);
    if(version<lastVersion||version<minimumVersion||(version===lastVersion&&stamp<lastServer)) return;
    const first=!snapshot; snapshot=next; lastVersion=version;lastServer=stamp;clockServer=stamp;clockAt=performance.now();
    $('room-workspace').hidden=false; $('room-workspace').setAttribute('aria-busy','false'); $('room-workspace').dataset.version=next.version;
    $('room-admin-config').hidden=true; $('room-loading').textContent='';$('room-title').textContent=next.title;$('lot-title').textContent=next.title;$('lot-location').textContent=next.content.location;
    $('room-meta').textContent='Phiên đấu giá';$('room-state').replaceChildren(P.node('span',labels[next.state]||Phase2.label(next.state),'p2-state'));$('room-state').dataset.state=next.state;
    
    const isClosed=next.state.startsWith('Closed');
    const isPrepared=next.state==='Prepared';

    // 1. Live vs Closed Price section handling (Eliminate duplicate price/winner boxes)
    const liveSection = $('live-price-section');
    const resultSection = $('room-result');
    const bidSection = $('bid-control-section');

    if (isClosed) {
      if (liveSection) liveSection.hidden = true;
      if (resultSection) {
        resultSection.hidden = false;
        $('result-price').textContent = money(next.result?.amount || next.currentPrice);
        const hasMineWinner = snapshot.bids?.some(b => b.isMine && b.bidder === next.result?.winner);
        const isWinnerMine = hasMineWinner || (snapshot.bids?.[0]?.isMine && next.result?.winner === snapshot.bids?.[0]?.bidder);
        const winnerName = isWinnerMine
          ? 'Bạn'
          : resolveBidder(next.result?.winner, false, true);
        $('result-text').textContent = next.result?.winner
          ? `Bên trúng đấu giá: ${winnerName} · Chốt lúc ${Phase2.time(next.result.closedAt)}`
          : 'Phiên kết thúc không có lượt trả giá hợp lệ. Không tạo hồ sơ giao dịch.';
        $('room-start-price-closed').textContent = money(next.rules.startingPrice);
        $('room-increment-closed').textContent = money(next.rules.minimumIncrement);
        $('total-bids-count-closed').textContent = `${next.bidCount} / ${next.extensionCount}`;
      }
      if (bidSection) bidSection.hidden = true;
    } else {
      if (resultSection) resultSection.hidden = true;
      if (liveSection) {
        liveSection.hidden = false;
        if (isPrepared) {
          $('price-status-label').textContent = 'Giá khởi điểm';
          $('current-top-price').textContent = money(next.rules.startingPrice);
          $('room-leader').textContent = 'Phiên chưa bắt đầu trả giá';
        } else {
          $('price-status-label').textContent = 'Giá đang dẫn đầu';
          $('current-top-price').textContent = money(next.currentPrice);
          const topBid = snapshot.bids?.[0];
          const isTopMine = topBid?.isMine;
          $('room-leader').textContent = next.leader
            ? `Dẫn đầu: ${resolveBidder(next.leader, isTopMine)}`
            : 'Chưa có doanh nghiệp dẫn đầu';
        }
        $('room-start-price').textContent = money(next.rules.startingPrice);
        $('room-increment').textContent = money(next.rules.minimumIncrement);
        $('total-bids-count').textContent = `${next.bidCount} / ${next.extensionCount}`;
      }
      if (bidSection) bidSection.hidden = isPrepared;
    }

    $('room-deadline').textContent=`Bắt đầu ${Phase2.time(next.startsAt)} · Hạn hiện tại ${Phase2.time(next.endsAt)}`;
    $('room-eligibility').textContent=eligibilityText(next);
    $('bid-minimum-label').textContent='Mức tối thiểu: '+money(next.minimumBid);
    $('room-start').hidden=user.role!=='Administrator'||next.state!=='Prepared';
    
    if(first) {
      Phase2.gallery($('room-images'),next.images);
      renderLotAttributes(next.content, next.categoryName);
      ruleList(next.rules);
      loadEnterpriseName().then(() => { if (snapshot) { bids(); render(snapshot); } });
    }
    bids(); tick();
  }
  async function history() {
    try {
      const items=await AgriApi.request(`/api/v1/auctions/${auctionId}/room/history`);
      const sorted = [...items].sort((a, b) => new Date(b.occurredAt) - new Date(a.occurredAt));
      $('room-history').replaceChildren(...sorted.map(e => {
        const li = P.node('li', undefined, 'p4-timeline-item');
        const time = P.node('time', Phase2.time(e.occurredAt), 'p4-timeline-time');
        const dot = P.node('span', undefined, 'p4-timeline-dot');
        const desc = P.node('span', eventLabels[e.kind] || 'Cập nhật phiên', 'p4-timeline-desc');
        li.append(time, dot, desc);
        return li;
      }));
      if ($('room-history-section')) $('room-history-section').hidden = !items.length;
    } catch(error) {
      if(error.status===401) {blocked=true;realtime('Unauthorized','Hết phiên đăng nhập');}
    }
  }
  async function sync() {
    if(blocked) return; if(fetching){queued=true;return;} fetching=true;
    try {render(await AgriApi.request(`/api/v1/auctions/${auctionId}/room`));}
    catch(error) {
      if(error.status===404&&!snapshot&&user.role==='Administrator') {
        try {auction=await AgriApi.request(`/api/v1/admin/auctions/${auctionId}`);$('room-admin-config').hidden=false;$('room-loading').textContent='Phòng chưa được cấu hình. Quản trị viên cần nhập đầy đủ quy tắc mô phỏng.';$('room-title').textContent=auction.content.title;$('room-meta').textContent=`Lịch đã công bố: ${Phase2.time(auction.terms.startsAt)} → ${Phase2.time(auction.terms.endsAt)}`;}
        catch(failure){Phase2.error(notice,failure);}
      } else {if(error.status===401||error.status===403) {blocked=true;realtime('Unauthorized','Không có quyền / hết phiên đăng nhập');} else if(error.status===404) $('room-loading').textContent='Chưa có phòng mô phỏng cho phiên này.';else {connected=false;$('room-loading').textContent='Không tải được dữ liệu phòng. Bấm Tải lại dữ liệu phòng để thử lại.';} Phase2.error(notice,error);tick();}
    } finally {fetching=false;if(queued){queued=false;await sync();}}
  }
  async function connect() {
    if(blocked||!snapshot||connection?.state!=='Disconnected'&&connection) return;
    try {await connection.start();await connection.invoke('JoinRoom',auctionId);await sync();if(blocked){await connection.stop();return;}realtime('Connected','Đã kết nối trực tiếp');await history();}
    catch(error) {realtime('Disconnected','Mất kết nối · đang thử lại');if(connection.state!=='Disconnected') await connection.stop();clearTimeout(retryTimer);retryTimer=setTimeout(connect,3000);}
  }
  function makeConnection() {
    connection=new signalR.HubConnectionBuilder().withUrl('/hubs/simulation',{transport:signalR.HttpTransportType.WebSockets}).withAutomaticReconnect([0,1000,2000,5000]).configureLogging(signalR.LogLevel.None).build();
    connection.on('AuctionChanged',change=>{if(change.auctionId!==auctionId) return;const v=BigInt(change.version);if(v<=lastVersion)return;minimumVersion=v>minimumVersion?v:minimumVersion;$('room-realtime').dataset.events=String(Number($('room-realtime').dataset.events)+1);$('room-realtime').dataset.lastEventVersion=change.version;sync().then(history);});
    connection.onreconnecting(()=>realtime('Reconnecting','Đang kết nối lại và cập nhật dữ liệu'));
    connection.onreconnected(async()=>{try {await connection.invoke('JoinRoom',auctionId);await sync();if(blocked){await connection.stop();return;}realtime('Connected','Đã kết nối lại và cập nhật dữ liệu');await history();}catch(error){realtime('Disconnected','Không thể vào lại phòng');await connection.stop();}});
    connection.onclose(()=>{realtime('Disconnected','Mất kết nối · đang tải lại dữ liệu');if(!blocked){clearTimeout(retryTimer);retryTimer=setTimeout(connect,3000);}});
    connect();
  }
  $('room-reload').addEventListener('click',async()=>{Phase2.message(notice,'');await sync();if(snapshot&&!connection)makeConnection();await history();});
  $('room-reconnect').addEventListener('click',async()=>{if(!connection)return;realtime('Reconnecting','Đang kết nối lại và cập nhật dữ liệu');await connection.stop();clearTimeout(retryTimer);await connect();});
  $('room-start').addEventListener('click',async()=>{if(busy||!snapshot)return;busy=true;tick();Phase2.message(notice,'Đang yêu cầu bắt đầu mô phỏng…');try {const result=await AgriApi.request(`/api/v1/admin/auctions/${auctionId}/room/start`,{method:'POST',body:{expectedVersion:snapshot.version}});render(result);Phase2.message(notice,'Quản trị viên đã bắt đầu mô phỏng.','success');await history();}catch(error){Phase2.error(notice,error);await sync();}finally{busy=false;tick();}});
  $('room-config-form').addEventListener('submit',async event=>{event.preventDefault();if(busy||!auction||!$('room-config-form').reportValidity())return;busy=true;$('room-configure').disabled=true;const rules=Object.fromEntries(new FormData($('room-config-form')));if(rules.priceBasis==='Giá áp dụng cho toàn bộ lô hàng')rules.priceBasis='whole lot simulation';for(const key of ['extensionWindowSeconds','extensionSeconds','maxExtensions'])rules[key]=rules[key]===''?null:Number(rules[key]);try {render(await AgriApi.request(`/api/v1/admin/auctions/${auctionId}/room`,{method:'POST',body:{expectedAuctionVersion:auction.version,rules}}));Phase2.message(notice,'Đã lưu quy tắc mô phỏng. Quản trị viên có thể bắt đầu theo lịch đã công bố.','success');makeConnection();}catch(error){Phase2.error(notice,error);Phase2.fieldErrors($('room-config-form'),error);}finally{busy=false;$('room-configure').disabled=false;tick();}});
  $('bid-minimum').addEventListener('click',()=>{if(snapshot&&!attempt)$('bid-input').value=snapshot.minimumBid;});
  $('bid-form').addEventListener('submit',async event=>{
    event.preventDefault();if(busy||!snapshot||$('btn-submit-bid').disabled||!event.currentTarget.reportValidity())return;busy=true;attempt ||= {key:crypto.randomUUID(),body:{amount:$('bid-input').value}};$('bid-input').readOnly=true;tick();Phase2.message($('bid-message'),'Đang gửi lượt trả giá, chờ hệ thống xác nhận…');
    try {const receipt=await AgriApi.request(`/api/v1/auctions/${auctionId}/room/bids`,{method:'POST',headers:{'Idempotency-Key':attempt.key},body:attempt.body});minimumVersion=BigInt(receipt.version)>minimumVersion?BigInt(receipt.version):minimumVersion;Phase2.message($('bid-message'),'Đã ghi nhận lượt trả giá.','success');attempt=null;$('bid-input').readOnly=false;await sync();await history();}
    catch(error) {Phase2.error($('bid-message'),error);Phase2.fieldErrors($('bid-form'),error);if(error.status===400||error.status===409){attempt=null;$('bid-input').readOnly=false;}await sync();}
    finally {busy=false;tick();}
  });
  $('bids-all').addEventListener('click',()=>{mineOnly=false;$('bids-all').setAttribute('aria-pressed','true');$('bids-mine').setAttribute('aria-pressed','false');if(snapshot)bids();});$('bids-mine').addEventListener('click',()=>{mineOnly=true;$('bids-all').setAttribute('aria-pressed','false');$('bids-mine').setAttribute('aria-pressed','true');if(snapshot)bids();});
  try {
    user=await Phase2.ready();
    try{if((await AgriApi.request('/api/v1/capabilities')).completion){
      const form=$('room-config-form');for(const [key,value]of Object.entries({currency:'VND',priceBasis:'Giá áp dụng cho toàn bộ lô hàng',firstBidRule:'AtLeastStartingPrice',incrementRule:'MinimumIncrease',tieRule:'FirstCommittedWins',extensionRule:'AddToDeadline',extensionWindowSeconds:'60',extensionSeconds:'60',maxExtensions:'3',closeRule:'AtOrAfterDeadline'}))if(form.elements[key])form.elements[key].value=value;
      olderButton=P.button('Tải lượt giá cũ hơn',async()=>{
        if(!snapshot||olderDone)return;
        olderButton.disabled=true;
        olderButton.textContent='Đang tải lịch sử…';
        try{
          const before=olderCursor||snapshot.bids.at(-1)?.version;
          if(!before)return;
          const data=await AgriApi.request(`/api/v1/auctions/${auctionId}/room/bids?pageSize=100&beforeVersion=${before}`);
          olderBids.push(...data.items);
          olderCursor=data.nextBeforeVersion;
          olderDone=!olderCursor;
          bids();
        }catch(error){
          Phase2.error(notice,error);
        }finally{
          olderButton.disabled=olderDone;
          olderButton.textContent=olderDone?'Đã tải hết lịch sử':'Tải lượt giá cũ hơn';
        }
      });
      olderButton.id = 'older-bids-btn';
      $('bid-history-state').after(olderButton);
    }}catch(error){if(error.status!==404)throw error;}
    if(!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(auctionId||'')) {$('room-loading').textContent='Chọn phòng từ đăng ký đã được quản trị viên xác nhận hoặc trang chuẩn bị phiên.';return;}
    await sync();if(snapshot)makeConnection();setInterval(tick,200);setInterval(async()=>{if(blocked||document.hidden)return;await sync();if(snapshot&&!connection)makeConnection();},2000);
    document.addEventListener('visibilitychange',()=>{if(!document.hidden)sync();});
  } catch(error){Phase2.error(notice,error);$('room-loading').textContent='Không thể truy cập phòng.';}
})();
