(function () {
  'use strict';
  const form = document.getElementById('login-form');
  const message = document.getElementById('login-message');
  const submit = document.getElementById('login-submit');
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (!form.reportValidity()) return;
    submit.disabled = true;
    message.textContent = 'Đang đăng nhập…';
    try {
      const result = await AgriApi.request('/api/v1/auth/login?useCookies=true', {
        method: 'POST', body: { email: form.elements.email.value, password: form.elements.password.value }
      });
      AgriApi.resetCsrf();
      form.elements.password.value = '';
      const homes = { Administrator: '/pages/admin/category-management.html', Farmer: '/pages/farmer/lots.html', Enterprise: '/pages/business/market-explorer.html' };
      const allowed = ['/pages/transactions.html', '/pages/notifications.html', '/pages/profile.html', '/pages/business/auction-room.html', ...(result.user.role === 'Administrator' ? ['/pages/admin/category-management.html', '/pages/admin/lot-inspection.html', '/pages/admin/auction-preparation.html', '/pages/admin/dispute-arbitration.html', '/pages/admin/analytics-dashboard.html'] : result.user.role === 'Farmer' ? ['/pages/farmer/lots.html', '/pages/business/dispute-claim.html'] : ['/pages/business/market-explorer.html', '/pages/business/deposit-wallet.html', '/pages/business/dispute-claim.html'])];
      const requested = new URLSearchParams(location.search).get('ReturnUrl');
      let target = homes[result.user.role] || '/pages/profile.html';
      if (requested) {
        try {
          const destination = new URL(requested, location.origin);
          if (destination.origin === location.origin && allowed.includes(destination.pathname)) target = destination.pathname + destination.search;
        } catch { /* A malformed return URL falls back to the role's home. */ }
      }
      window.location.assign(target);
    } catch (error) {
      message.textContent = error.message;
    } finally { submit.disabled = false; }
  });
  document.getElementById('login-logout').addEventListener('click', async () => {
    try {
      await AgriApi.request('/api/v1/auth/logout', { method: 'POST' });
      AgriApi.resetCsrf();
      message.textContent = 'Đã đăng xuất.';
      document.getElementById('login-logout').hidden = true;
    } catch (error) { message.textContent = error.message; }
  });
})();
