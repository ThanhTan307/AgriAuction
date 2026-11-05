(async function () {
  'use strict';
  const ui = Phase2, api = AgriApi, form = document.getElementById('profile-form'), message = document.getElementById('page-message');
  const save = document.getElementById('profile-save'), name = document.getElementById('profile-name'), reload = document.getElementById('profile-reload');
  let profile, busy = false;
  function render(result) { profile = result; name.value = profile.displayName; document.getElementById('profile-email').value = profile.email; document.getElementById('profile-role').value = ui.role(profile.role); document.querySelectorAll('[data-user-name]').forEach(node => { node.textContent = profile.displayName; }); }
  function pending(value) { busy = value; name.disabled = value || !profile; save.disabled = value || !profile; reload.disabled = value; document.getElementById('profile-card').setAttribute('aria-busy', String(value)); }
  async function load() {
    if (busy) return; pending(true); ui.message(message, 'Đang tải hồ sơ…');
    try { render(await api.request('/api/v1/me/profile')); ui.message(message, ''); ui.fieldErrors(form); }
    catch (failure) { profile = null; ui.error(message, failure); }
    finally { pending(false); }
  }
  reload.addEventListener('click', load);
  form.addEventListener('submit', async event => {
    event.preventDefault(); if (busy || !profile || !form.reportValidity()) return;
    pending(true); ui.message(message, 'Đang lưu…'); ui.fieldErrors(form);
    try { render(await api.request('/api/v1/me/profile', { method: 'PATCH', body: { displayName: name.value, expectedVersion: profile.version } })); ui.message(message, 'Hồ sơ đã được lưu.', 'success'); }
    catch (failure) { ui.fieldErrors(form, failure); ui.error(message, failure); }
    finally { pending(false); }
  });
  try { await ui.ready(); await load(); } catch (failure) { ui.error(message, failure); }
})();
