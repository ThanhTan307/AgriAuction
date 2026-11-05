(async function () {
  'use strict';
  const api = AgriApi, ui = Phase2, $ = id => document.getElementById(id), message = $('page-message'), form = $('inspection-form');
  let selected, busy = false, checklistReady = false, page = 1, total = 0, listNumber = 0, detailNumber = 0, debounce;
  function controls() {
    const enabled = selected?.state === 'PendingReview' && !busy;
    $('inspection-reason').disabled = !enabled; $('inspection-approve').disabled = !enabled || !checklistReady; $('inspection-reject').disabled = !enabled;
    for(const id of ['inspection-approve','inspection-reject']) $(id).title = enabled ? '' : busy ? 'Đang lưu quyết định, vui lòng chờ.' : selected ? 'Hồ sơ này đã có quyết định; không thể duyệt lại.' : 'Chọn hồ sơ đang chờ kiểm duyệt.';
    let hint=document.getElementById('inspection-control-hint');if(!hint){hint=document.createElement('p');hint.id='inspection-control-hint';hint.className='p2-muted';form.append(hint);}hint.textContent=enabled?'':$('inspection-approve').title;
    $('inspection-prev').disabled = busy || page <= 1; $('inspection-next').disabled = busy || page * 20 >= total;
    $('inspection-reload').disabled = busy;
    $('inspection-queue').querySelectorAll('button').forEach(node => { node.disabled = busy; });
    const checklistFieldset = document.getElementById('completion-checklist');
    if (checklistFieldset) {
      checklistFieldset.hidden = !enabled;
    }
  }
  async function loadChecklist() {
    const capabilities = await api.request('/api/v1/capabilities');
    const fieldset = document.getElementById('completion-checklist') || document.createElement('fieldset');
    fieldset.id = 'completion-checklist';
    fieldset.className = 'inspection-checklist';
    fieldset.replaceChildren(Object.assign(document.createElement('legend'), { textContent: 'Tiêu chí cần kiểm tra trước khi phê duyệt' }));
    form.prepend(fieldset);
    if (!capabilities.completion) {
      checklistReady = true;
      return;
    }
    const items = await api.request('/api/v1/workflow-catalog?kind=Inspection');
    for (const item of items) {
      const label = document.createElement('label');
      const input = document.createElement('input');
      input.type = 'checkbox';
      input.value = item.id;
      input.dataset.checklistItem = '';
      input.className = 'inspection-checklist-input';
      label.className = 'inspection-checklist-item';
      label.append(input, document.createTextNode(` ${item.name}`));
      fieldset.append(label);
    }
    if (!items.length) {
      const notice = document.createElement('p');
      notice.className = 'inspection-checklist-empty';
      notice.textContent = 'Chưa có checklist hoạt động. Hãy cấu hình tại Danh mục dùng chung trước khi phê duyệt.';
      fieldset.append(notice);
    }
    checklistReady = true;
  }
  function render(lot, draft) {
    selected = lot; $('inspection-detail').hidden = false; $('inspection-detail-state').hidden = true;
    $('inspection-title').textContent = lot.content.title;
    $('inspection-meta').replaceChildren(document.createTextNode(`Nông dân: ${lot.farmerName} • Gửi lúc ${ui.time(lot.submittedAt)} `), ui.reference(lot.id));
    $('inspection-status').replaceChildren(ui.badge(lot.state));
    ui.attributes($('inspection-attributes'), lot.content, lot.categoryName); ui.gallery($('inspection-images'), lot.images);
    const revision = lot.revisions.find(item => item.id === lot.currentRevisionId);
    $('inspection-snapshot').textContent = revision ? `Bản gửi lần ${revision.number}, lúc ${ui.time(revision.submittedAt)}. Quyết định gắn với bản gửi này.` : 'Chưa có bản gửi duyệt.';
    $('inspection-submitted-attributes').replaceChildren();
    if (revision) ui.attributes($('inspection-submitted-attributes'), revision.content, lot.categoryName);
    ui.history($('inspection-history'), lot);
    form.reset();
    if (draft && lot.state === 'PendingReview' &&
        lot.version === draft.version &&
        lot.currentRevisionId === draft.currentRevisionId) {
      $('inspection-reason').value = draft.reason;
    }
    ui.fieldErrors(form);
    history.replaceState(null, '', '/pages/admin/lot-inspection.html?lotId=' + lot.id);
    controls();
  }
  async function open(id) {
    if (busy) return; const number = ++detailNumber; busy = true; controls(); ui.message(message, 'Đang tải hồ sơ lô…');
    const draft = selected?.id === id && selected.state === 'PendingReview'
      ? {
          version: selected.version,
          currentRevisionId: selected.currentRevisionId,
          reason: $('inspection-reason').value
        }
      : null;
    $('inspection-detail').setAttribute('aria-busy', 'true');
    try { const lot = await api.request('/api/v1/admin/lots/' + encodeURIComponent(id)); if (number === detailNumber) { render(lot, draft); ui.message(message, ''); } }
    catch (failure) { if (number === detailNumber) { selected = null; $('inspection-detail').hidden = true; ui.error(message, failure); } }
    finally { if (number === detailNumber) { busy = false; $('inspection-detail').setAttribute('aria-busy', 'false'); controls(); } }
  }
  async function loadQueue() {
    const number = ++listNumber; $('inspection-queue').setAttribute('aria-busy', 'true');
    $('inspection-prev').disabled = true; $('inspection-next').disabled = true;
    try {
      const result = await api.request(`/api/v1/admin/lots/pending?query=${encodeURIComponent($('inspection-search').value)}&page=${page}&pageSize=20`);
      if (number !== listNumber) return; total = result.total;
      $('inspection-state').textContent = total ? `${total} hồ sơ phù hợp đang chờ kiểm duyệt` : 'Không có hồ sơ chờ duyệt phù hợp.';
      $('inspection-page').textContent = 'Trang ' + page;
      const rows = document.createDocumentFragment();
      for (const lot of result.items) {
        const button = document.createElement('button'); button.type = 'button'; button.className = 'p2-lot-row'; button.dataset.lotId = lot.id;
        button.setAttribute('aria-current', String(selected?.id === lot.id));
        const title = document.createElement('strong'); title.textContent = lot.title;
        const meta = document.createElement('div'); meta.className = 'p2-muted'; meta.textContent = `${lot.farmerName} • Gửi ${ui.time(lot.submittedAt)}`;
        button.append(title, ui.badge(lot.state), meta); button.addEventListener('click', () => open(lot.id)); rows.append(button);
      }
      $('inspection-queue').replaceChildren(rows);
    } catch (failure) { if (number === listNumber) { total = 0; $('inspection-state').textContent = 'Không thể tải hàng đợi. Dùng nút tải lại để thử lại.'; ui.error(message, failure); } }
    finally { if (number === listNumber) { $('inspection-queue').setAttribute('aria-busy', 'false'); controls(); } }
  }
  form.addEventListener('submit', async event => {
    event.preventDefault(); if (busy || selected?.state !== 'PendingReview' || !form.reportValidity()) return;
    const outcome = event.submitter?.dataset.outcome; if (!['Approve', 'Reject'].includes(outcome)) return;
    if (outcome === 'Approve' && !checklistReady) return;
    busy = true; controls(); ui.message(message, 'Đang lưu quyết định…'); ui.fieldErrors(form);
    try {
      render(await api.request(`/api/v1/admin/lots/${selected.id}/decision`, { method: 'POST', body: { expectedVersion: selected.version, reviewedRevisionId: selected.currentRevisionId, outcome, reason: $('inspection-reason').value,checklistItems:[...document.querySelectorAll('[data-checklist-item]:checked')].map(n=>n.value) } }));
      ui.message(message, 'Quyết định đã được lưu. Nông dân có thể xem kết quả trong hồ sơ lô.', 'success'); await loadQueue();
    } catch (failure) { ui.fieldErrors(form, failure); ui.error(message, failure); }
    finally { busy = false; controls(); }
  });
  $('inspection-reload').addEventListener('click', async () => { ui.message(message, ''); await loadQueue(); if (selected) await open(selected.id); });
  $('inspection-search').addEventListener('input', () => { clearTimeout(debounce); debounce = setTimeout(() => { page = 1; loadQueue(); }, 300); });
  $('inspection-prev').addEventListener('click', () => { if (page > 1) { page--; loadQueue(); } });
  $('inspection-next').addEventListener('click', () => { if (page * 20 < total) { page++; loadQueue(); } });
  try {
    await ui.ready('Administrator');
    await loadChecklist();
    await loadQueue();
    const requested = new URLSearchParams(location.search).get('lotId');
    if (requested && /^[0-9a-f-]{36}$/i.test(requested)) await open(requested);
    const refresh = () => { if (!document.hidden && !busy) { loadQueue(); if (selected) open(selected.id); } };
    const timer = setInterval(refresh, 10000);
    document.addEventListener('visibilitychange', refresh);
    window.addEventListener('focus', refresh);
    window.addEventListener('pagehide', () => { clearInterval(timer); }, { once: true });
  }
  catch (failure) { ui.error(message, failure); $('inspection-state').textContent = 'Chưa tải được dữ liệu kiểm duyệt.'; }
  finally { controls(); }
})();
