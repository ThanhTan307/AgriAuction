(function () {
  'use strict';
  // Bump when the shared HTML shell changes; CSS/JS hashes alone cannot refresh cached HTML.
  const shellVersion = '20261102-ux1';
  function pageUrl(value) { const url = new URL(value, location.origin); url.searchParams.set('ui', shellVersion); return url.pathname + url.search; }
  const paths = {
    accounts:'/pages/admin/accounts.html',
    dashboard: '/pages/admin/analytics-dashboard.html', inspection: '/pages/admin/lot-inspection.html',
    preparation: '/pages/admin/auction-preparation.html', categories: '/pages/admin/category-management.html',
    arbitration: '/pages/admin/dispute-arbitration.html', lots: '/pages/farmer/lots.html',
    market: '/pages/business/market-explorer.html', registrations: '/pages/business/deposit-wallet.html',
    room: '/pages/business/auction-room.html', transactions: '/pages/transactions.html',
    notifications: '/pages/notifications.html', complaints: '/pages/business/dispute-claim.html', profile: '/pages/profile.html'
  };
  const entries = {
    accounts:['Duyệt tài khoản','manage_accounts'],
    dashboard: ['Tổng quan & Báo cáo', 'monitoring'], inspection: ['Kiểm duyệt lô hàng', 'fact_check'],
    preparation: ['Chuẩn bị phiên mô phỏng', 'event'], categories: ['Danh mục dùng chung', 'category'],
    arbitration: ['Tranh chấp mô phỏng', 'balance'], lots: ['Lô nông sản của tôi', 'agriculture'],
    market: ['Sàn tra cứu nông sản', 'storefront'], registrations: ['Đăng ký & cọc mô phỏng', 'account_balance_wallet'],
    room: ['Phòng đấu giá mô phỏng', 'gavel'], transactions: ['Hồ sơ giao dịch', 'receipt_long'],
    notifications: ['Thông báo', 'notifications'], complaints: ['Khiếu nại & giải trình', 'forum'], profile: ['Hồ sơ cá nhân', 'person']
  };
  const menus = {
    Administrator: ['dashboard', 'accounts', 'inspection', 'preparation', 'categories', 'arbitration', 'transactions', 'notifications', 'profile'],
    Farmer: ['lots', 'transactions', 'notifications', 'complaints', 'profile'],
    Enterprise: ['market', 'registrations', 'room', 'transactions', 'notifications', 'complaints', 'profile']
  };
  const roleNames = { Administrator: 'Quản trị viên', Farmer: 'Nông dân', Enterprise: 'Doanh nghiệp' };
  const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  const narrow = matchMedia('(max-width: 1023px)');
  let promise, sidebar, toggle, backdrop, main, notice, actor, drawerOpen = false, collapsed = false;
  let unreadTimer = null, unreadRequestPending = false;
  function node(tag, text, className) {
    const element = document.createElement(tag); if (text !== undefined) element.textContent = text;
    if (className) element.className = className; return element;
  }
  function icon(name) { const element = node('span', name, 'material-symbols-outlined'); element.setAttribute('aria-hidden', 'true'); return element; }
  function state() {
    const hidden = narrow.matches ? !drawerOpen : collapsed;
    document.body.classList.toggle('app-drawer-open', narrow.matches && drawerOpen);
    document.body.classList.toggle('app-nav-collapsed', !narrow.matches && collapsed);
    sidebar.inert = hidden; main.inert = narrow.matches && drawerOpen;
    toggle.setAttribute('aria-expanded', String(!hidden));
    backdrop.hidden = !(narrow.matches && drawerOpen);
  }
  function closeDrawer(returnFocus = false) { drawerOpen = false; state(); if (returnFocus) toggle.focus(); }
  function message(text) { notice.textContent = text; notice.hidden = !text; }
  function setUnreadCount(value) {
    const count = Number.isFinite(Number(value)) ? Math.max(0, Math.floor(Number(value))) : 0;
    const link = sidebar?.querySelector('[data-nav-key="notifications"]');
    const badge = link?.querySelector('.app-nav-badge');
    if (link && badge) {
      const badgeText = count > 99 ? '99+' : String(count);
      if (badge.textContent !== badgeText) badge.textContent = badgeText;
      badge.hidden = count === 0;
      badge.style.display = count === 0 ? 'none' : 'inline-flex';
      const label = count ? `Thông báo, ${count} chưa đọc` : entries.notifications[0];
      if (link.getAttribute('aria-label') !== label) link.setAttribute('aria-label', label);
      link.title = count ? `${count} thông báo chưa đọc` : entries.notifications[0];
    }
  }
  async function refreshUnreadCount() {
    const expectedActor = actor, expectedId = actor?.id;
    if (!expectedId || location.pathname === paths.notifications || !['Administrator', 'Enterprise', 'Farmer'].includes(expectedActor.role) || unreadRequestPending) return;
    const link = sidebar?.querySelector('[data-nav-key="notifications"]');
    if (!link) return;
    unreadRequestPending = true;
    try {
      const result = await AgriApi.request('/api/v1/me/notifications/unread-count');
      if (actor?.id === expectedId && sidebar?.querySelector('[data-nav-key="notifications"]') === link)
        setUnreadCount(result.unreadCount);
    } catch { /* Badge polling must never change session state or block navigation. */ }
    finally {
      unreadRequestPending = false;
      if (actor?.id && actor.id !== expectedId) void refreshUnreadCount();
    }
  }
  function startUnreadPolling() {
    if (unreadTimer || !actor || location.pathname === paths.notifications) return;
    void refreshUnreadCount();
    unreadTimer = setInterval(() => { if (!document.hidden) void refreshUnreadCount(); }, 10000);
  }
  function stopUnreadPolling() {
    if (unreadTimer) clearInterval(unreadTimer);
    unreadTimer = null;
  }
  function frame() {
    if (sidebar) return;
    const root = document.getElementById('app-navigation');
    if (!root) throw new Error('Thiếu vùng điều hướng dùng chung.');
    main = document.querySelector('main'); if (!main.id) main.id = 'app-main-content'; main.tabIndex = -1;
    const skip = node('a', 'Đến nội dung chính', 'app-skip-link'); skip.href = '#' + main.id;
    sidebar = node('aside', undefined, 'app-sidebar'); sidebar.id = 'app-sidebar'; sidebar.setAttribute('aria-label', 'Điều hướng hệ thống');
    const brand = node('div', undefined, 'app-brand');
    const wordmark = node('div', undefined, 'app-brand-lockup'); wordmark.append(node('strong', 'AgriAuction'), node('small', 'SÀN ĐẤU GIÁ NÔNG SẢN')); brand.append(wordmark);
    const close = node('button', undefined, 'app-drawer-close'); close.type = 'button'; close.setAttribute('aria-label', 'Đóng menu'); close.append(icon('close')); close.addEventListener('click', () => closeDrawer(true)); brand.append(close);
    const nav = node('nav', undefined, 'app-menu'); nav.setAttribute('aria-label', 'Menu chính'); nav.id = 'app-role-menu';
    const footer = node('div', undefined, 'app-account');
    const name = node('span', 'Đang tải tài khoản…'); name.dataset.userName = ''; name.title = 'Đang tải tài khoản…';
    const role = node('small'); role.dataset.userRole = ''; footer.append(name, role);
    sidebar.append(brand, nav, footer);
    const header = node('header', undefined, 'app-topbar'); header.id = 'app-header';
    const left = node('div', undefined, 'app-header-left');
    toggle = node('button', undefined, 'app-menu-toggle'); toggle.id = 'app-menu-toggle'; toggle.type = 'button'; toggle.setAttribute('aria-label', 'Ẩn/hiện menu'); toggle.setAttribute('aria-controls', sidebar.id); toggle.dataset.toggleSidebar = ''; toggle.append(icon('menu'));
    toggle.addEventListener('click', () => { if (narrow.matches) { drawerOpen = !drawerOpen; state(); if (drawerOpen) close.focus(); } else { collapsed = !collapsed; state(); } });
    const headerBrand = node('span', undefined, 'app-header-brand');
    headerBrand.append(node('strong', 'AgriAuction'));
    left.append(toggle, headerBrand, node('span', undefined, 'app-page-title'));
    const right = node('div', undefined, 'app-header-right');
    const profile = node('a', 'Đang tải…', 'app-header-user'); profile.href = paths.profile; profile.dataset.userName = ''; right.append(profile);
    const logout = node('button', 'Đăng xuất', 'app-logout'); logout.type = 'button'; logout.dataset.logout = '';
    logout.addEventListener('click', async () => {
      logout.disabled = true; message('');
      try {
        await AgriApi.request('/api/v1/auth/logout', { method: 'POST' }); actor = null; stopUnreadPolling(); setUnreadCount(0); AgriApi.resetCsrf();
        try { sessionStorage.removeItem('agri:room:' + actor.id); } catch { /* Storage may be disabled. */ }
        location.assign('/pages/login.html');
      } catch (error) { message(error.message); logout.disabled = false; }
    });
    right.append(logout); header.append(left, right);
    backdrop = node('button', undefined, 'app-nav-backdrop'); backdrop.type = 'button'; backdrop.tabIndex = -1; backdrop.setAttribute('aria-label', 'Đóng menu'); backdrop.addEventListener('click', () => closeDrawer(true));
    notice = node('p', undefined, 'app-navigation-notice'); notice.setAttribute('role', 'status'); notice.hidden = true;
    root.replaceChildren(skip, sidebar, header, backdrop, notice);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) void refreshUnreadCount(); });
    window.addEventListener('focus', () => void refreshUnreadCount());
    window.addEventListener('pagehide', stopUnreadPolling);
    window.addEventListener('pageshow', startUnreadPolling);
    narrow.addEventListener('change', () => { drawerOpen = false; state(); });
    document.addEventListener('keydown', event => {
      if (!narrow.matches || !drawerOpen) return;
      if (event.key === 'Escape') { event.preventDefault(); closeDrawer(true); }
      else if (event.key === 'Tab') {
        const items = [...sidebar.querySelectorAll('a[href],button:not(:disabled)')];
        if (event.shiftKey && document.activeElement === items[0]) { event.preventDefault(); items.at(-1).focus(); }
        else if (!event.shiftKey && document.activeElement === items.at(-1)) { event.preventDefault(); items[0].focus(); }
      }
    });
    state();
  }
  async function roomDestination(user) {
    const fallback = paths.registrations + '?chooseRoom=1', key = 'agri:room:' + user.id;
    let candidate = location.pathname === paths.room ? new URLSearchParams(location.search).get('auctionId') : null;
    try { candidate ||= sessionStorage.getItem(key); } catch { /* Continue without saved context. */ }
    if (!uuid.test(candidate || '')) return fallback;
    try {
      const room = await AgriApi.request('/api/v1/auctions/' + candidate + '/room', { signal: AbortSignal.timeout(10000) });
      if (room.auctionId.toLowerCase() !== candidate.toLowerCase()) return fallback;
      try { sessionStorage.setItem(key, candidate); } catch { /* Current-page navigation still works. */ }
      return paths.room + '?auctionId=' + encodeURIComponent(candidate);
    } catch (error) {
      if (error.status === 403 || error.status === 404) try { sessionStorage.removeItem(key); } catch { /* Storage disabled. */ }
      return fallback;
    }
  }
  async function render(user) {
    if (actor?.id && actor.id !== user.id) setUnreadCount(0);
    actor = user;
    if (!menus[user.role]) throw new AgriApi.ApiError(403, { title: 'Vai trò không có điều hướng được cấp quyền.' });
    toggle.setAttribute('aria-controls', sidebar.id);
    document.querySelectorAll('[data-user-name]').forEach(e => {
      const displayName = user.displayName || roleNames[user.role];
      e.textContent = displayName;
      e.title = displayName;
      e.setAttribute('aria-label', displayName);
    });
    document.querySelectorAll('[data-user-role]').forEach(e => { e.textContent = roleNames[user.role]; });
    const current = Object.keys(paths).find(key => paths[key] === location.pathname);
    const active = current === 'room' && user.role !== 'Enterprise' ? user.role === 'Administrator' ? 'preparation' : 'lots' : current;
    document.querySelector('.app-page-title').textContent = entries[current]?.[0] || 'AgriAuction';
    sidebar.dataset.role = user.role;
    const nav = document.getElementById('app-role-menu'); nav.replaceChildren();
    let completion=false;
    if(user.role==='Administrator')try{completion=(await AgriApi.request('/api/v1/capabilities')).completion===true;}catch{/* Legacy host keeps its existing menu. */}
    for (const key of menus[user.role]) {
      if(key==='accounts'&&!completion)continue;
      const a = node('a'); a.dataset.navKey = key; a.href = pageUrl(key === 'room' ? paths.registrations + '?chooseRoom=1' : paths[key]);
      a.append(icon(entries[key][1]), node('span', entries[key][0]));
      if (key === 'notifications') { const badge = node('span', '0', 'app-nav-badge'); badge.hidden = true; badge.style.display = 'none'; badge.setAttribute('aria-hidden', 'true'); a.append(badge); a.setAttribute('aria-label', entries[key][0]); }
      if (key === active) a.setAttribute('aria-current', 'page');
      a.addEventListener('click', () => { if (narrow.matches) closeDrawer(); }); nav.append(a);
    }
    if (user.role === 'Enterprise') {
      nav.querySelector('[data-nav-key="room"]').href = pageUrl(await roomDestination(user));
      if (location.pathname === paths.registrations && new URLSearchParams(location.search).get('chooseRoom') === '1') {
        const hint = node('p', 'Chọn đăng ký đã được quản trị viên xác nhận, rồi bấm “Vào phòng mô phỏng”.', 'app-room-help'); hint.setAttribute('role', 'status'); main.prepend(hint);
      }
    }
    document.getElementById('app-navigation').setAttribute('aria-busy', 'false');
    startUnreadPolling();
  }
  async function ready(role) {
    frame();
    if (!promise) promise = (async () => {
      const user = await AgriApi.request('/api/v1/me'); await render(user); return user;
    })().catch(error => {
      promise = null; message(error.message || 'Không thể tải điều hướng. Hãy tải lại trang.');
      if (error.status === 401) location.assign('/pages/login.html?ReturnUrl=' + encodeURIComponent(location.pathname + location.search));
      throw error;
    });
    const user = await promise;
    if (role && user.role !== role) {
      const roleText = roleNames[role] || role;
      const userRoleText = roleNames[user.role] || user.role;
      const title = `Trang này dành cho vai trò ${roleText}. Tài khoản hiện tại: "${user.displayName || user.email}" (${userRoleText}). Lưu ý: nếu demo nhiều tài khoản, hãy sử dụng cửa sổ Ẩn danh (Incognito) hoặc hồ sơ trình duyệt riêng để tránh ghi đè phiên đăng nhập.`;
      message(title);
      throw new AgriApi.ApiError(403, { title });
    }
    document.querySelectorAll('[data-for-role]').forEach(e => { e.hidden = !e.dataset.forRole.split(',').includes(user.role); });
    return user;
  }
  window.AppNavigation = { ready, roleName: value => roleNames[value] || 'Thành viên', updateUnreadCount: setUnreadCount };
})();
