(function () {
  'use strict';
  const api = window.AgriApi;
  const tbody = document.getElementById('category-rows');
  const state = document.getElementById('category-state');
  const notice = document.getElementById('category-notice');
  const dialog = document.getElementById('category-dialog');
  const form = document.getElementById('category-form');
  const search = document.getElementById('category-search');
  const save = document.getElementById('category-save');
  let items = [];
  let selected;
  let page = 1;
  let requestNumber = 0;
  let busy = false;
  let searchTimer;
  const date = value => new Intl.DateTimeFormat('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', dateStyle: 'short', timeStyle: 'short' }).format(new Date(value));

  function message(element, text, kind = '') {
    element.textContent = text;
    element.dataset.kind = kind;
    element.hidden = !text;
  }
  function stateRow(text) {
    tbody.replaceChildren();
    const tr = document.createElement('tr');
    const td = document.createElement('td');
    td.colSpan = 7;
    td.className = 'px-4 py-8 text-center text-slate-500';
    td.textContent = text;
    tr.append(td);
    tbody.append(tr);
  }
  function render(rows) {
    tbody.replaceChildren();
    if (!rows.length) { stateRow('Chưa có danh mục phù hợp.'); return; }
    for (const item of rows) {
      const tr = document.createElement('tr');
      tr.className = 'hover:bg-slate-50';
      for (const value of [item.code, item.name, item.active ? 'Đang sử dụng' : 'Ngừng sử dụng', item.version, date(item.createdAt), date(item.updatedAt)]) {
        const td = document.createElement('td');
        td.className = 'px-4 py-3 text-slate-700';
        td.textContent = String(value);
        tr.append(td);
      }
      const td = document.createElement('td');
      td.className = 'px-4 py-3 text-center';
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = 'Sửa';
      button.className = 'text-slate-600 font-medium hover:text-emerald-700 hover:underline';
      button.addEventListener('click', () => openEditor(item));
      td.append(button);
      tr.append(td);
      tbody.append(tr);
    }
  }
  function errorText(error) {
    const details = Object.values(error.problem?.errors || {}).flat().join(' ');
    return `${error.message}${details ? ' ' + details : ''}${error.problem?.traceId ? ' Mã tra cứu: ' + error.problem.traceId : ''}`;
  }
  function handleAuth(error) {
    if (error.status === 401) { window.location.assign('/pages/login.html'); return true; }
    if (error.status === 403) {
      document.getElementById('category-add').disabled = true;
      stateRow('Bạn không có quyền quản trị danh mục.');
      return true;
    }
    return false;
  }
  async function load() {
    const number = ++requestNumber;
    tbody.setAttribute('aria-busy', 'true');
    message(state, 'Đang tải danh mục…');
    stateRow('Đang tải…');
    document.getElementById('category-prev').disabled = true;
    document.getElementById('category-next').disabled = true;
    try {
      const data = await api.request(`/api/v1/admin/catalog/categories?query=${encodeURIComponent(search.value.trim())}&page=${page}&pageSize=20`);
      if (number !== requestNumber) return false;
      items = data.items;
      render(items);
      document.getElementById('category-total').textContent = data.allCount;
      document.getElementById('category-active').textContent = data.activeCount;
      document.getElementById('category-inactive').textContent = data.inactiveCount;
      document.getElementById('category-connection').textContent = 'Đã cập nhật';
      document.getElementById('category-page').textContent = `Trang ${page} · ${data.totalCount} kết quả`;
      document.getElementById('category-prev').disabled = page <= 1;
      document.getElementById('category-next').disabled = page * 20 >= data.totalCount;
      message(state, data.totalCount ? '' : 'Chưa có danh mục phù hợp.');
      return true;
    } catch (error) {
      if (number !== requestNumber) return false;
      handleAuth(error);
      items = [];
      stateRow(error.status === 403 ? 'Không có quyền truy cập.' : 'Không tải được dữ liệu. Vui lòng thử lại.');
      document.getElementById('category-connection').textContent = 'Chưa cập nhật';
      for (const id of ['category-total', 'category-active', 'category-inactive']) document.getElementById(id).textContent = '—';
      message(state, errorText(error), 'error');
      return false;
    } finally { if (number === requestNumber) tbody.setAttribute('aria-busy', 'false'); }
  }
  function openEditor(item) {
    if (busy) return;
    selected = item;
    form.reset();
    form.elements.code.value = item?.code || '';
    form.elements.name.value = item?.name || '';
    form.elements.active.value = String(item?.active ?? true);
    document.getElementById('category-dialog-title').textContent = item ? 'Sửa danh mục' : 'Thêm danh mục';
    document.querySelectorAll('[data-field-error]').forEach(element => { element.textContent = ''; });
    message(document.getElementById('category-form-error'), '');
    dialog.showModal();
    form.elements.code.focus();
  }
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (busy || !form.reportValidity()) return;
    busy = true;
    save.disabled = true;
    document.getElementById('category-cancel').disabled = true;
    document.querySelectorAll('[data-field-error]').forEach(element => { element.textContent = ''; });
    message(document.getElementById('category-form-error'), '');
    message(notice, '');
    const body = { code: form.elements.code.value, name: form.elements.name.value, active: form.elements.active.value === 'true' };
    if (selected) body.expectedVersion = selected.version;
    try {
      await api.request('/api/v1/admin/catalog/categories' + (selected ? '/' + selected.id : ''), { method: selected ? 'PATCH' : 'POST', body });
      dialog.close();
      const reloaded = await load();
      message(notice, reloaded ? 'Đã lưu danh mục và cập nhật danh sách.' : 'Đã lưu danh mục, nhưng chưa tải lại được danh sách. Hãy nhấn Tải lại dữ liệu.', reloaded ? 'success' : 'error');
    } catch (error) {
      handleAuth(error);
      message(document.getElementById('category-form-error'), errorText(error), 'error');
      for (const [field, errors] of Object.entries(error.problem?.errors || {})) {
        const element = document.querySelector(`[data-field-error="${field === 'code' || field === 'name' || field === 'active' ? field : 'other'}"]`);
        if (element) element.textContent = errors.join(' ');
      }
    } finally { busy = false; save.disabled = false; document.getElementById('category-cancel').disabled = false; }
  });
  document.getElementById('category-add').addEventListener('click', () => openEditor());
  document.getElementById('category-cancel').addEventListener('click', () => { if (!busy) dialog.close(); });
  dialog.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
  document.getElementById('category-reload').addEventListener('click', load);
  document.getElementById('category-prev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
  document.getElementById('category-next').addEventListener('click', () => { page++; load(); });
  search.addEventListener('input', () => { clearTimeout(searchTimer); searchTimer = setTimeout(() => { page = 1; load(); }, 300); });
  (async () => {
    try {
      await AppNavigation.ready('Administrator');
      await load();
    } catch (error) { handleAuth(error); message(state, errorText(error), 'error'); }
  })();
})();
