(async function () {
  'use strict';
  const api = AgriApi, ui = Phase2, $ = id => document.getElementById(id);
  const form = $('lot-form'), imageForm = $('lot-image-form'), message = $('page-message');
  let selected = null, categories = [], busy = false, ready = false, dirty = false, page = 1, total = 0, listNumber = 0, detailNumber = 0, debounce;
  const editable = () => !selected || ['Draft', 'Rejected'].includes(selected.state);
  function controls() {
    $('lot-fields').disabled = busy || !ready || !editable();
    $('lot-save').disabled = busy || !ready || !editable();
    $('lot-new').disabled = busy || !ready;
    $('lot-refresh').disabled = busy || !selected;
    $('lot-submit').disabled = busy || dirty || !selected || !editable();
    $('lot-image').disabled = busy || dirty || !selected || !editable();
    $('lot-image-upload').disabled = busy || dirty || !selected || !editable();
    const reason = busy ? 'Đang xử lý, vui lòng chờ.' : !ready ? 'Chờ tải thông tin lô và danh mục.' : !editable() ? 'Lô đang chờ duyệt hoặc đã được duyệt; không thể sửa nội dung và ảnh.' : dirty ? 'Lưu bản nháp trước khi tải ảnh hoặc gửi kiểm duyệt.' : !selected ? 'Lưu bản nháp trước khi tải ảnh hoặc gửi kiểm duyệt.' : '';
    for(const id of ['lot-submit','lot-image-upload','lot-save']) $(id).title = $(id).disabled ? reason : '';
    let hint=document.getElementById('lot-control-hint');if(!hint){hint=document.createElement('p');hint.id='lot-control-hint';hint.className='p2-muted';form.append(hint);}hint.textContent=reason;
    $('lot-prev').disabled = busy || page <= 1;
    $('lot-next').disabled = busy || page * 20 >= total;
    $('lot-list').querySelectorAll('button').forEach(node => { node.disabled = busy; });
    $('lot-images').querySelectorAll('button').forEach(node => { node.disabled = busy || dirty; });
    $('lot-list-reload').disabled = busy;
    form.setAttribute('aria-busy', String(busy));
  }
  function localDate(value) {
    const date = new Date(value); return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
  }
  function fill(result) {
    selected = result; dirty = false; form.reset(); imageForm.reset(); ui.fieldErrors(document);
    $('lot-status').replaceChildren();
    const obsolete = $('lot-category').querySelector('[data-obsolete]'); obsolete?.remove();
    if (result) {
      $('lot-editor-title').textContent = result.content.title;
      $('lot-meta').replaceChildren(document.createTextNode(`Cập nhật ${ui.time(result.updatedAt)} `), ui.reference(result.id));
      $('lot-status').append(ui.badge(result.state));
      if (!categories.some(category => category.id === result.content.categoryId)) {
        const option = document.createElement('option'); option.value = result.content.categoryId; option.textContent = result.categoryName + ' (đã ngừng sử dụng)'; option.dataset.obsolete = 'true'; $('lot-category').append(option);
      }
      for (const [key, value] of Object.entries(result.content)) {
        const input = form.elements.namedItem(key); if (input) input.value = key.endsWith('At') ? (value ? localDate(value) : '') : value ?? '';
      }
      ui.gallery($('lot-images'), result.images, editable() ? detach : null); ui.history($('lot-history'), result);
      const last = result.reviews.at(-1);
      $('lot-detail-state').textContent = last ? `${last.outcome === 'Approve' ? 'Phê duyệt' : 'Từ chối'}: ${last.reason}` : editable() ? 'Bạn có thể sửa bản nháp, tải ảnh rồi gửi duyệt.' : 'Hồ sơ đang chờ quản trị viên kiểm duyệt; dữ liệu gửi được giữ nguyên.';
      history.replaceState(null, '', '/pages/farmer/lots.html?lotId=' + result.id);
    } else {
      $('lot-editor-title').textContent = 'Tạo lô mới'; $('lot-meta').textContent = '';
      $('lot-detail-state').textContent = 'Điền thông tin lô; sau khi lưu nháp bạn có thể tải ảnh.';
      $('lot-images').replaceChildren(); $('lot-history').textContent = 'Chưa có lần gửi duyệt.';
      history.replaceState(null, '', '/pages/farmer/lots.html');
    }
    document.querySelectorAll('[data-lot-id]').forEach(node => node.setAttribute('aria-current', String(node.dataset.lotId === result?.id)));
    controls();
  }
  async function loadList(refreshSelected = true) {
    const number = ++listNumber; $('lot-list').setAttribute('aria-busy', 'true'); $('lot-list-state').textContent = 'Đang tải danh sách…';
    $('lot-prev').disabled = true; $('lot-next').disabled = true;
    try {
      const result = await api.request(`/api/v1/lots?query=${encodeURIComponent($('lot-search').value)}&page=${page}&pageSize=20`);
      if (number !== listNumber) return;
      total = result.total; $('lot-list-state').textContent = total ? `${total} lô phù hợp` : 'Chưa có lô phù hợp. Bạn có thể tạo một bản nháp mới.';
      $('lot-page').textContent = `Trang ${page}`;
      const rows = document.createDocumentFragment();
      for (const item of result.items) {
        const node = document.createElement('button'); node.type = 'button'; node.className = 'p2-lot-row'; node.dataset.lotId = item.id;
        node.setAttribute('aria-current', String(selected?.id === item.id));
        const title = document.createElement('strong'); title.textContent = item.title;
        const meta = document.createElement('div'); meta.className = 'p2-muted'; meta.textContent = 'Cập nhật ' + ui.time(item.updatedAt);
        node.append(title, ui.badge(item.state), meta); node.addEventListener('click', () => open(item.id)); rows.append(node);
      }
      $('lot-list').replaceChildren(rows);
      // List and editor are separate API snapshots. Refresh the selected detail too,
      // unless that would discard an in-progress edit or overlap a mutation.
      if (refreshSelected && selected && !busy) {
        if (dirty) ui.message(message, 'Danh sách đã tải lại. Nội dung chưa lưu được giữ nguyên; lưu hoặc tải lại lô đang chọn để cập nhật chi tiết.');
        else await open(selected.id);
      }
    } catch (failure) { if (number === listNumber) { total = 0; $('lot-list-state').textContent = 'Không tải được danh sách. Dùng nút tải lại để thử lại.'; ui.error(message, failure); } }
    finally { if (number === listNumber) { $('lot-list').setAttribute('aria-busy', 'false'); controls(); } }
  }
  async function open(id) {
    if (busy) return; const number = ++detailNumber; busy = true; controls(); ui.message(message, 'Đang tải hồ sơ lô…');
    try { const result = await api.request('/api/v1/lots/' + encodeURIComponent(id)); if (number === detailNumber) { fill(result); ui.message(message, ''); } }
    catch (failure) { ui.error(message, failure); }
    finally { if (number === detailNumber) { busy = false; controls(); } }
  }
  async function mutate(action, success) {
    if (busy) return; busy = true; controls(); ui.message(message, 'Đang xử lý…'); ui.fieldErrors(document);
    try { fill(await action()); ui.message(message, success, 'success'); await loadList(); }
    catch (failure) { ui.fieldErrors(document, failure); ui.error(message, failure); }
    finally { busy = false; controls(); }
  }
  function content() {
    const result = Object.fromEntries(new FormData(form));
    for (const key of ['proposedStartsAt', 'proposedEndsAt']) result[key] = new Date(result[key]).toISOString();
    return result;
  }
  form.addEventListener('input', () => { dirty = true; if (selected) $('lot-detail-state').textContent = 'Có thay đổi chưa lưu. Hãy lưu bản nháp trước khi đổi ảnh hoặc gửi kiểm duyệt.'; controls(); });
  form.addEventListener('submit', event => {
    event.preventDefault(); if (busy || !editable() || !form.reportValidity()) return;
    const payload = content();
    mutate(() => api.request('/api/v1/lots' + (selected ? '/' + selected.id : ''), {
      method: selected ? 'PATCH' : 'POST', body: selected ? { content: payload, expectedVersion: selected.version } : { content: payload }
    }), 'Bản nháp lô đã được lưu.');
  });
  imageForm.addEventListener('submit', event => {
    event.preventDefault(); if (busy || !selected || !editable() || !imageForm.reportValidity()) return;
    const file = $('lot-image').files[0], body = new FormData(); body.append('file', file); body.append('expectedVersion', selected.version);
    mutate(() => api.request(`/api/v1/lots/${selected.id}/images`, { method: 'POST', body }), 'Ảnh đã được lưu và gắn vào lô.');
  });
  function detach(id) { if (selected && editable()) mutate(() => api.request(`/api/v1/lots/${selected.id}/images/${id}`, { method: 'DELETE', body: { expectedVersion: selected.version } }), 'Ảnh đã được gỡ khỏi bản nháp; lịch sử gửi trước được giữ nguyên.'); }
  $('lot-submit').addEventListener('click', () => { if (selected && editable()) mutate(() => api.request(`/api/v1/lots/${selected.id}/submit`, { method: 'POST', body: { expectedVersion: selected.version } }), 'Lô đã gửi kiểm duyệt. Bạn có thể tải lại để xem kết quả.'); });
  $('lot-new').addEventListener('click', () => { if (!busy) { fill(null); ui.message(message, ''); } });
  $('lot-refresh').addEventListener('click', async () => { if (selected) { await open(selected.id); await loadList(false); } });
  $('lot-list-reload').addEventListener('click', () => { ui.message(message, ''); loadList(); });
  $('lot-search').addEventListener('input', () => { clearTimeout(debounce); debounce = setTimeout(() => { page = 1; loadList(); }, 300); });
  $('lot-prev').addEventListener('click', () => { if (page > 1) { page--; loadList(); } });
  $('lot-next').addEventListener('click', () => { if (page * 20 < total) { page++; loadList(); } });
  try {
    await ui.ready('Farmer');
    let categoryPage = 1, result;
    do { result = await api.request(`/api/v1/catalog/categories?page=${categoryPage}&pageSize=100`); categories.push(...result.items); categoryPage++; } while (categories.length < result.totalCount);
    for (const category of categories) { const option = document.createElement('option'); option.value = category.id; option.textContent = category.name; $('lot-category').append(option); }
    ready = true; const requested = new URLSearchParams(location.search).get('lotId'); fill(null); await loadList();
    if (requested && /^[0-9a-f-]{36}$/i.test(requested)) await open(requested);
    const refresh = () => { if (!document.hidden && !busy) { loadList(false); if (selected) open(selected.id); } };
    const timer = setInterval(refresh, 10000);
    document.addEventListener('visibilitychange', refresh);
    window.addEventListener('focus', refresh);
    window.addEventListener('pagehide', () => { clearInterval(timer); }, { once: true });
  } catch (failure) { ui.error(message, failure); $('lot-list-state').textContent = 'Không thể tải dữ liệu. Hãy tải lại trang sau khi kiểm tra phiên đăng nhập.'; }
  finally { controls(); }
})();
