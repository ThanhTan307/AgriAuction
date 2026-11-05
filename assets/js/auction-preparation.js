(async function () {
  'use strict';
  const P = Phase3, $ = id => document.getElementById(id), notice = $('page-message');
  let lots = [], auctionPage = 1, registrationPage = 1, query = '', selected = null, attempt = null;
  let busy = false, auctionSerial = 0, regSerial = 0;
  let allAuctions = [], allRegistrations = [], auctionsMap = new Map();
  let regFilterAuction = '', regFilterStatus = '', regFilterQuery = '';
  let activeTab = 'auctions';

  P.init();
  $('prepare-depositAmount').min = '0.0001';
  $('prepare-depositAmount').inputMode = 'decimal';

  // --- TAB MANAGEMENT (Text / Underline) ---
  const tabs = {
    auctions: { btn: $('tab-btn-auctions'), pane: $('tab-pane-auctions'), hash: '#danh-sach-phien' },
    registrations: { btn: $('tab-btn-registrations'), pane: $('tab-pane-registrations'), hash: '#dang-ky-coc' },
    create: { btn: $('tab-btn-create'), pane: $('tab-pane-create'), hash: '#tao-phien' }
  };

  function switchTab(targetKey, updateHash = true) {
    if (!tabs[targetKey]) return;
    activeTab = targetKey;
    for (const [key, tab] of Object.entries(tabs)) {
      const isActive = key === targetKey;
      tab.btn.classList.toggle('active', isActive);
      tab.btn.setAttribute('aria-selected', isActive ? 'true' : 'false');
      tab.pane.hidden = !isActive;
    }
    if (updateHash && location.hash !== tabs[targetKey].hash) {
      history.replaceState(null, '', tabs[targetKey].hash);
    }
  }

  $('tab-btn-auctions').addEventListener('click', () => switchTab('auctions'));
  $('tab-btn-registrations').addEventListener('click', () => switchTab('registrations'));
  $('tab-btn-create').addEventListener('click', () => switchTab('create'));

  $('btn-goto-registrations')?.addEventListener('click', () => {
    switchTab('registrations');
    $('reg-filter-status').value = 'PendingDeposit';
    regFilterStatus = 'PendingDeposit';
    applyRegistrationFilters();
  });

  if (location.hash === '#dang-ky-coc') activeTab = 'registrations';
  else if (location.hash === '#tao-phien') activeTab = 'create';
  switchTab(activeTab, false);

  // --- APPROVED LOTS ---
  async function approved() {
    $('prepare-submit').disabled = $('prepare-lot').disabled = true;
    try {
      lots = await AgriApi.request('/api/v1/admin/auctions/approved-lots');
      $('prepare-lot').replaceChildren(new Option('Chọn lô đã phê duyệt', ''));
      for (const lot of lots) {
        $('prepare-lot').append(new Option(`${lot.title} · ${lot.farmerName}`, lot.id));
      }

      const count = lots.length;
      const noLotsAlert = $('no-lots-alert');
      if (noLotsAlert) noLotsAlert.hidden = count > 0;

      if (count > 0) {
        $('approved-state').textContent = `${count} lô đã phê duyệt có thể tạo phiên.`;
        $('prepare-submit').disabled = $('prepare-lot').disabled = false;
      } else {
        $('approved-state').textContent = '';
        $('prepare-submit').disabled = $('prepare-lot').disabled = true;
      }
    } catch (error) {
      $('approved-state').textContent = 'Không tải được danh sách lô đã phê duyệt.';
      Phase2.error(notice, error);
    }
  }

  // --- AUCTION DETAIL DIALOG ---
  function showAuctionDetail(a) {
    const root = $('auction-detail-content');
    root.replaceChildren();
    const sched = P.primaryStatus(a);

    const title = P.node('h3', a.content.title);
    title.style.fontSize = '16px';
    title.style.margin = '0 0 4px';

    const sub = P.node('p', `Nông dân: ${a.farmerName || '—'} · Địa phương: ${a.content.location || '—'} · Loại: ${a.categoryName || '—'}`, 'p2-muted');
    sub.style.marginBottom = '12px';

    const statusBadge = P.node('span', sched.text, 'p2-state');
    statusBadge.dataset.state = sched.state;
    statusBadge.style.marginBottom = '12px';

    const termsDl = P.terms(a);
    termsDl.style.margin = '14px 0';

    const techMeta = P.node('details', undefined, 'p3-detail-meta');
    techMeta.style.padding = '8px 12px';
    techMeta.style.background = '#f8fafc';
    techMeta.style.borderRadius = '4px';
    techMeta.style.marginTop = '12px';
    const techSummary = P.node('summary', 'Chi tiết kỹ thuật');
    techMeta.replaceChildren(techSummary);
    for (const [label, value] of [['Mã phiên', a.id], ['Mã lô', a.lotId]]) {
      const row = P.node('p'); row.append(P.node('span', `${label}: `));
      const code = P.node('code', value); row.append(code);
      const copy = P.node('button', 'Sao chép', 'p2-secondary'); copy.type = 'button';
      copy.addEventListener('click', async () => { try { await navigator.clipboard.writeText(String(value)); copy.textContent = 'Đã sao chép'; } catch { copy.textContent = 'Không thể sao chép'; } });
      row.append(copy); techMeta.append(row);
    }
    techMeta.open = false;

    root.append(title, sub, statusBadge, termsDl, techMeta);
    $('auction-detail-dialog').showModal();
  }

  // --- AUCTIONS LIST (Structured Table) ---
  async function auctions() {
    const serial = ++auctionSerial;
    const tbody = $('admin-auction-tbody');
    $('admin-auction-list').setAttribute('aria-busy', 'true');
    $('auction-prev').disabled = $('auction-next').disabled = true;

    try {
      const data = await AgriApi.request(`/api/v1/admin/auctions?page=${auctionPage}&pageSize=10&query=${encodeURIComponent(query)}`);
      if (serial !== auctionSerial) return;

      allAuctions = data.items || [];
      for (const a of allAuctions) {
        auctionsMap.set(a.id, a);
      }

      updateAuctionFilterDropdown();

      const rows = document.createDocumentFragment();
      for (const a of allAuctions) {
        const tr = P.node('tr');
        tr.dataset.auctionId = a.id;
        const sched = P.primaryStatus(a);

        // 1. Lot Title & Farmer
        const tdTitle = P.node('td');
        const titleStrong = P.node('strong', a.content.title);
        titleStrong.style.display = 'block';
        const subFarmer = P.node('small', `${a.farmerName || '—'} · ${a.categoryName || '—'}`);
        subFarmer.style.color = '#64748b';
        tdTitle.append(titleStrong, subFarmer);
        tr.append(tdTitle);

        // 2. Registration window
        const tdReg = P.node('td');
        tdReg.append(P.timeRange(a.terms.registrationOpensAt, a.terms.registrationClosesAt));
        tr.append(tdReg);

        // 3. Auction window
        const tdAuction = P.node('td');
        tdAuction.append(P.timeRange(a.terms.startsAt, a.terms.endsAt));
        tr.append(tdAuction);

        // 4. Deposit (căn phải, tabular-nums)
        const tdDep = P.node('td');
        tdDep.className = 'p3-col-num';
        tdDep.append(P.node('strong', `${Phase2.decimal(a.terms.depositAmount)} VND`));
        tr.append(tdDep);

        // 5. Single primary status badge
        const tdStatus = P.node('td');
        const badge = P.node('span', sched.text, 'p2-state');
        badge.dataset.state = sched.state;
        tdStatus.append(badge);
        tr.append(tdStatus);

        // 6. Action
        const tdAction = P.node('td');
        tdAction.className = 'p3-col-actions';
        const actionGroup = P.node('div', undefined, 'p2-actions');
        actionGroup.style.margin = '0';
        actionGroup.style.gap = '6px';
        actionGroup.style.justifyContent = 'flex-end';

        actionGroup.append(P.button('Chi tiết', () => showAuctionDetail(a), 'p2-secondary'));

        if (sched.canPublish) {
          actionGroup.append(P.button('Công bố phiên', async event => {
            const button = event.currentTarget;
            button.disabled = true;
            Phase2.message(notice, 'Đang công bố phiên…');
            try {
              await AgriApi.request(`/api/v1/admin/auctions/${a.id}/publish-simulation`, {
                method: 'POST',
                body: { expectedVersion: a.version }
              });
              Phase2.message(notice, 'Đã công bố phiên thành công.', 'success');
              await auctions();
              await registrations();
            } catch (error) {
              Phase2.error(notice, error);
              button.disabled = false;
            }
          }, true));
        } else if (sched.isEnded) {
          const viewLink = P.node('a', 'Xem phòng đã kết thúc', 'p2-secondary text-xs');
          viewLink.href = '/pages/business/auction-room.html?auctionId=' + a.id;
          actionGroup.append(viewLink);
        } else {
          const roomLink = P.node('a', 'Vào phòng đấu giá', 'p2-secondary text-xs');
          roomLink.href = '/pages/business/auction-room.html?auctionId=' + a.id;
          actionGroup.append(roomLink);
        }

        tdAction.append(actionGroup);
        tr.append(tdAction);
        rows.append(tr);
      }

      tbody.replaceChildren(rows);
      $('admin-auction-state').textContent = data.items.length ? '' : 'Chưa có phiên phù hợp.';
      P.pager(data, $('auction-prev'), $('auction-next'), $('auction-page'));
    } catch (error) {
      if (serial === auctionSerial) {
        $('admin-auction-state').textContent = 'Không tải được danh sách phiên.';
        Phase2.error(notice, error);
      }
    } finally {
      if (serial === auctionSerial) $('admin-auction-list').setAttribute('aria-busy', 'false');
    }
  }

  function updateAuctionFilterDropdown() {
    const select = $('reg-filter-auction');
    if (!select) return;
    const currentVal = select.value;
    select.replaceChildren(new Option('Tất cả các phiên', ''));
    for (const a of allAuctions) {
      select.append(new Option(a.content.title, a.id));
    }
    select.value = currentVal;
  }

  // --- REGISTRATIONS & DEPOSIT CONFIRMATION ---
  function confirm(r, forceExpired = false) {
    selected = r;
    attempt = null;
    const auction = auctionsMap.get(r.auctionId);
    const sched = auction ? P.primaryStatus(auction) : null;
    const isExpired = forceExpired || (sched ? sched.isEnded : false);

    $('confirm-reason').value = '';
    $('confirm-reason').readOnly = isExpired;
    $('confirm-key').value = crypto.randomUUID();
    $('confirm-submit').disabled = isExpired;

    $('confirm-target').textContent = `${r.auctionTitle} · ${r.enterpriseName} · ${P.amount(r.deposit.amount, r.deposit.currency)}`;

    if (isExpired) {
      Phase2.message($('confirm-message'), 'Phiên đã hết hạn đăng ký. Không thể xác nhận cọc.', 'error');
    } else {
      Phase2.message($('confirm-message'), '');
    }
    Phase2.fieldErrors($('confirm-form'));
    $('confirm-dialog').showModal();
  }

  async function registrations() {
    const serial = ++regSerial;
    $('registration-list').setAttribute('aria-busy', 'true');
    $('page-prev').disabled = $('page-next').disabled = true;

    try {
      const data = await AgriApi.request(`/api/v1/admin/registrations?page=${registrationPage}&pageSize=50`);
      if (serial !== regSerial) return;

      allRegistrations = data.items || [];

      // Calculate actionable pending registrations (only those strictly within an open registration window)
      const now = Date.now();
      let actionablePendingCount = 0;
      for (const r of allRegistrations) {
        if (r.state === 'PendingDeposit') {
          const auction = auctionsMap.get(r.auctionId);
          if (auction) {
            const opensAt = new Date(auction.terms.registrationOpensAt).getTime();
            const closesAt = new Date(auction.terms.registrationClosesAt).getTime();
            if (now >= opensAt && now < closesAt) {
              actionablePendingCount++;
            }
          }
        }
      }

      // Only show urgent banner when actionable registrations exist (> 0)
      const urgentBanner = $('urgent-pending-banner');
      const urgentCount = $('urgent-pending-count');
      if (urgentBanner) {
        if (actionablePendingCount > 0) {
          if (urgentCount) urgentCount.textContent = actionablePendingCount;
          urgentBanner.hidden = false;
          urgentBanner.style.display = 'flex';
        } else {
          urgentBanner.hidden = true;
          urgentBanner.style.display = 'none';
        }
      }

      applyRegistrationFilters(data);
      P.pager(data, $('page-prev'), $('page-next'), $('page-number'));
    } catch (error) {
      if (serial === regSerial) {
        $('registration-state').textContent = 'Không tải được danh sách đăng ký.';
        Phase2.error(notice, error);
      }
    } finally {
      if (serial === regSerial) $('registration-list').setAttribute('aria-busy', 'false');
    }
  }

  function applyRegistrationFilters() {
    let filtered = allRegistrations;

    if (regFilterAuction) {
      filtered = filtered.filter(r => r.auctionId === regFilterAuction);
    }
    if (regFilterStatus) {
      filtered = filtered.filter(r => r.state === regFilterStatus || r.deposit?.state === regFilterStatus || r.deposit?.settlementKind === regFilterStatus);
    }
    if (regFilterQuery) {
      const q = regFilterQuery.toLowerCase();
      filtered = filtered.filter(r => (r.enterpriseName || '').toLowerCase().includes(q) || (r.auctionTitle || '').toLowerCase().includes(q));
    }

    const summaryLine = $('reg-summary-line');
    if (summaryLine) {
      summaryLine.textContent = `Tổng số: ${filtered.length} hồ sơ đăng ký.`;
    }

    const displayData = {
      items: filtered,
      total: filtered.length,
      page: registrationPage,
      pageSize: 50
    };

    P.registrationRows(displayData, true, confirm, auctionsMap);

    if (filtered.length === 0 && allRegistrations.length > 0) {
      $('registration-state').textContent = 'Không có hồ sơ nào khớp với bộ lọc.';
    }
  }

  // Filter change listeners
  $('reg-filter-auction')?.addEventListener('change', e => {
    regFilterAuction = e.target.value;
    applyRegistrationFilters();
  });
  $('reg-filter-status')?.addEventListener('change', e => {
    regFilterStatus = e.target.value;
    applyRegistrationFilters();
  });
  $('reg-filter-query')?.addEventListener('input', e => {
    regFilterQuery = e.target.value.trim();
    applyRegistrationFilters();
  });
  $('reg-filter-reset')?.addEventListener('click', () => {
    regFilterAuction = '';
    regFilterStatus = '';
    regFilterQuery = '';
    $('reg-filter-auction').value = '';
    $('reg-filter-status').value = '';
    $('reg-filter-query').value = '';
    applyRegistrationFilters();
  });

  // --- FORM CREATE SUBMISSION & VALIDATION ---
  $('prepare-form').addEventListener('submit', async event => {
    event.preventDefault();
    if (busy || !$('prepare-form').reportValidity()) return;

    const lot = lots.find(l => l.id === $('prepare-lot').value);
    if (!lot) {
      Phase2.message(notice, 'Vui lòng chọn một lô nông sản đã phê duyệt.', 'error');
      return;
    }

    const regOpenDate = new Date($('prepare-registrationOpensAt').value);
    const regCloseDate = new Date($('prepare-registrationClosesAt').value);
    const startDate = new Date($('prepare-startsAt').value);
    const endDate = new Date($('prepare-endsAt').value);

    Phase2.fieldErrors($('prepare-form'));

    let hasDateError = false;
    if (regCloseDate <= regOpenDate) {
      Phase2.fieldErrors($('prepare-form'), {
        problem: { errors: { registrationClosesAt: ['Thời gian đóng đăng ký phải sau thời gian mở đăng ký.'] } }
      });
      hasDateError = true;
    }
    if (startDate < regCloseDate) {
      Phase2.fieldErrors($('prepare-form'), {
        problem: { errors: { startsAt: ['Thời gian bắt đầu đấu giá phải bằng hoặc sau thời gian đóng đăng ký.'] } }
      });
      hasDateError = true;
    }
    if (endDate <= startDate) {
      Phase2.fieldErrors($('prepare-form'), {
        problem: { errors: { endsAt: ['Thời gian kết thúc phải sau thời gian bắt đầu đấu giá.'] } }
      });
      hasDateError = true;
    }

    if (hasDateError) {
      Phase2.message(notice, 'Thứ tự mốc thời gian chưa hợp lý.', 'error');
      return;
    }

    const depositAmount = Phase2.parseDecimalInput($('prepare-depositAmount').value);
    if (depositAmount === null || depositAmount <= 0) {
      Phase2.fieldErrors($('prepare-form'), {
        problem: { errors: { depositAmount: ['Nhập số tiền cọc dương hợp lệ.'] } }
      });
      Phase2.message(notice, 'Số tiền cọc chưa đúng định dạng.', 'error');
      return;
    }

    const currency = $('prepare-currency').value.trim().toUpperCase() || 'VND';

    busy = true;
    $('prepare-submit').disabled = true;
    Phase2.message(notice, 'Đang lưu bản nháp phiên…');

    const terms = {
      registrationOpensAt: regOpenDate.toISOString(),
      registrationClosesAt: regCloseDate.toISOString(),
      startsAt: startDate.toISOString(),
      endsAt: endDate.toISOString(),
      depositAmount: depositAmount,
      currency: currency
    };

    try {
      await AgriApi.request('/api/v1/admin/auctions', {
        method: 'POST',
        body: { lotId: lot.id, expectedLotVersion: lot.version, terms }
      });

      $('prepare-form').reset();
      $('prepare-currency').value = 'VND';
      auctionPage = 1;
      Phase2.message(notice, 'Đã lưu bản nháp phiên thành công.', 'success');
      await Promise.all([approved(), auctions()]);
      switchTab('auctions');
    } catch (error) {
      Phase2.error(notice, error);
      Phase2.fieldErrors($('prepare-form'), error);
    } finally {
      busy = false;
      $('prepare-submit').disabled = !lots.length;
    }
  });

  // --- CONFIRM DEPOSIT FORM SUBMISSION ---
  $('confirm-form').addEventListener('submit', async event => {
    event.preventDefault();
    if (busy || !selected || !$('confirm-form').reportValidity()) return;

    busy = true;
    $('confirm-submit').disabled = true;
    $('confirm-reason').readOnly = true;

    attempt ||= {
      key: $('confirm-key').value,
      body: { expectedVersion: selected.version, reason: $('confirm-reason').value }
    };
    Phase2.message($('confirm-message'), 'Đang gửi xác nhận cọc…');

    try {
      await AgriApi.request(`/api/v1/admin/registrations/${selected.id}/confirm-simulation`, {
        method: 'POST',
        headers: { 'Idempotency-Key': attempt.key },
        body: attempt.body
      });
      $('confirm-dialog').close();
      Phase2.message(notice, 'Đã xác nhận cọc thành công.', 'success');
      await registrations();
    } catch (error) {
      if (error.problem?.code === 'REGISTRATION_CLOSED' || /Ngoài thời gian đăng ký/i.test(error.message || '')) {
        Phase2.message($('confirm-message'), 'Không thể xác nhận: Đã hết thời hạn đăng ký của phiên.', 'error');
      } else {
        Phase2.error($('confirm-message'), error);
      }
      Phase2.fieldErrors($('confirm-form'), error);

      if (error.status === 400) {
        attempt = null;
        $('confirm-reason').readOnly = false;
        $('confirm-key').value = crypto.randomUUID();
      }
    } finally {
      busy = false;
      $('confirm-submit').disabled = false;
    }
  });

  // --- CONTROLS & LISTENERS ---
  $('admin-auctions-reload').addEventListener('click', async () => {
    Phase2.message(notice, '');
    await approved();
    await auctions();
    await registrations();
  });

  $('admin-search-form').addEventListener('submit', event => {
    event.preventDefault();
    query = $('admin-auction-query').value.trim();
    auctionPage = 1;
    auctions();
  });

  $('auction-prev').addEventListener('click', () => { auctionPage--; auctions(); });
  $('auction-next').addEventListener('click', () => { auctionPage++; auctions(); });
  $('page-prev').addEventListener('click', () => { registrationPage--; registrations(); });
  $('page-next').addEventListener('click', () => { registrationPage++; registrations(); });

  // --- INITIALIZATION & REFRESH ---
  try {
    await Phase2.ready('Administrator');
    await approved();
    await auctions();
    await registrations();

    const refresh = async () => {
      if (!document.hidden) {
        await approved();
        await auctions();
        await registrations();
      }
    };
    const timer = setInterval(refresh, 10000);
    document.addEventListener('visibilitychange', refresh);
    window.addEventListener('focus', refresh);
    window.addEventListener('pagehide', () => { clearInterval(timer); }, { once: true });
  } catch (error) {
    Phase2.error(notice, error);
    $('approved-state').textContent = $('admin-auction-state').textContent = $('registration-state').textContent = 'Không thể truy cập dữ liệu.';
    $('admin-auction-list').setAttribute('aria-busy', 'false');
    $('registration-list').setAttribute('aria-busy', 'false');
  }
})();
