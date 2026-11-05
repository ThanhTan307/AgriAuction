(async function () {
  'use strict';
  const node = (tag, text, className) => {
    const element = document.createElement(tag);
    if (text !== undefined) element.textContent = text;
    if (className) element.className = className;
    return element;
  };

  try {
    if (!(await AgriApi.request('/api/v1/capabilities')).completion) return;
    await AppNavigation.ready();

    if (location.pathname.endsWith('/category-management.html')) {
      const tabs = {
        categories: { btn: document.getElementById('tab-btn-categories'), pane: document.getElementById('tab-pane-categories'), hash: '#loai-nong-san' },
        inspection: { btn: document.getElementById('tab-btn-inspection'), pane: document.getElementById('tab-pane-inspection'), hash: '#checklist-kiem-duyet' },
        complaints: { btn: document.getElementById('tab-btn-complaints'), pane: document.getElementById('tab-pane-complaints'), hash: '#ly-do-khieu-nai' }
      };
      function switchTab(targetKey) {
        if (!tabs[targetKey]) return;
        for (const [key, tab] of Object.entries(tabs)) {
          if (!tab.btn || !tab.pane) continue;
          const isActive = key === targetKey;
          tab.btn.classList.toggle('active', isActive);
          tab.btn.setAttribute('aria-selected', String(isActive));
          tab.pane.hidden = !isActive;
        }
        if (location.hash !== tabs[targetKey].hash) {
          history.replaceState(null, '', tabs[targetKey].hash);
        }
      }
      tabs.categories.btn?.addEventListener('click', () => switchTab('categories'));
      tabs.inspection.btn?.addEventListener('click', () => switchTab('inspection'));
      tabs.complaints.btn?.addEventListener('click', () => switchTab('complaints'));

      window.addEventListener('hashchange', () => {
        if (location.hash === '#checklist-kiem-duyet') switchTab('inspection');
        else if (location.hash === '#ly-do-khieu-nai') switchTab('complaints');
        else if (location.hash === '#loai-nong-san') switchTab('categories');
      });

      if (location.hash === '#checklist-kiem-duyet') switchTab('inspection');
      else if (location.hash === '#ly-do-khieu-nai') switchTab('complaints');

      async function group(kind, title, subtitle, targetId) {
        const container = document.getElementById(targetId);
        if (!container) return;
        container.replaceChildren();

        const titleDiv = node('div');
        titleDiv.className = 'p3-title';
        titleDiv.style.marginBottom = '14px';
        const h2 = node('h2', title);
        h2.style.fontSize = '16px';
        h2.style.fontWeight = '600';
        h2.style.margin = '0';
        const sub = node('p', subtitle, 'p2-muted');
        sub.style.margin = '2px 0 0';
        sub.style.fontSize = '12px';
        const headerText = node('div');
        headerText.append(h2, sub);

        const itemName = kind === 'Inspection' ? 'tiêu chí' : (kind === 'ComplaintReason' ? 'lý do' : 'mục');

        // Add form toolbar
        const form = node('form');
        form.className = 'p2-toolbar workflow-catalog-create';
        const input = node('input');
        input.type = 'text';
        input.id = 'catalog-' + kind;
        input.required = true;
        input.maxLength = 200;
        input.placeholder = `Nhập tên ${itemName} mới…`;
        input.setAttribute('aria-label', `Tên ${itemName} mới`);
        const add = node('button', `+ Thêm ${itemName}`);
        add.type = 'submit';
        add.className = 'p2-button';
        form.append(input, add);

        titleDiv.append(headerText, form);
        container.append(titleDiv);

        const message = node('p', undefined, 'p2-notice');
        message.hidden = true;
        container.append(message);

        // Table container
        const tableWrap = node('div');
        tableWrap.className = 'workflow-catalog-table';

        // Table header
        const thead = node('div');
        thead.className = 'workflow-table-head';
        const thTitle = node('div', `Nội dung ${itemName}`);
        thTitle.className = 'workflow-col-title';
        const thStatus = node('div', 'Trạng thái');
        thStatus.className = 'workflow-col-status';
        const thAction = node('div', 'Thao tác');
        thAction.className = 'workflow-col-actions';
        thead.append(thTitle, thStatus, thAction);

        // Table body
        const listWrap = node('div');
        listWrap.className = 'workflow-table-body';
        tableWrap.append(thead, listWrap);

        container.append(tableWrap);

        async function load() {
          listWrap.replaceChildren();
          const items = await AgriApi.request('/api/v1/workflow-catalog?kind=' + kind);
          if (!items.length) {
            const emptyP = node('p', 'Chưa có mục nào trong danh mục này.', 'p2-muted');
            emptyP.style.padding = '16px';
            emptyP.style.textAlign = 'center';
            listWrap.append(emptyP);
            return;
          }
          for (const item of items) {
            const row = node('form');
            row.className = 'workflow-catalog-row';

            const colTitle = node('div');
            colTitle.className = 'workflow-col-title';
            const name = node('input');
            name.type = 'text';
            name.value = item.name;
            name.required = true;
            name.maxLength = 200;
            name.setAttribute('aria-label', `Tên ${itemName}`);
            colTitle.append(name);

            const colStatus = node('div');
            colStatus.className = 'workflow-col-status';
            const activeWrap = node('label');
            activeWrap.className = 'p2-checkbox-label';
            const active = node('input');
            active.type = 'checkbox';
            active.checked = item.active;
            active.id = 'active-' + item.id;
            const activeSpan = node('span', 'Đang sử dụng');
            activeWrap.append(active, activeSpan);
            colStatus.append(activeWrap);

            const colActions = node('div');
            colActions.className = 'workflow-col-actions';
            const save = node('button', 'Lưu thay đổi');
            save.type = 'submit';
            save.className = 'p2-secondary workflow-catalog-save';
            colActions.append(save);

            row.append(colTitle, colStatus, colActions);

            row.addEventListener('submit', async event => {
              event.preventDefault();
              save.disabled = true;
              try {
                await AgriApi.request('/api/v1/workflow-catalog/' + item.id, {
                  method: 'PATCH',
                  body: { kind, name: name.value, active: active.checked, expectedVersion: item.version }
                });
                message.textContent = 'Đã lưu thay đổi.';
                message.dataset.kind = 'success';
                message.hidden = false;
                setTimeout(() => { message.hidden = true; }, 3000);
                await load();
              } catch (error) {
                message.textContent = error.message;
                message.dataset.kind = 'error';
                message.hidden = false;
                save.disabled = false;
              }
            });
            listWrap.append(row);
          }
        }

        form.addEventListener('submit', async event => {
          event.preventDefault();
          add.disabled = true;
          try {
            await AgriApi.request('/api/v1/workflow-catalog', {
              method: 'POST', body: { kind, name: input.value, active: true }
            });
            input.value = '';
            message.textContent = `Đã thêm ${itemName} mới.`;
            message.dataset.kind = 'success';
            message.hidden = false;
            setTimeout(() => { message.hidden = true; }, 3000);
            await load();
          } catch (error) {
            message.textContent = error.message;
            message.dataset.kind = 'error';
            message.hidden = false;
          } finally {
            add.disabled = false;
          }
        });
        await load();
      }

      await group('Inspection', 'Tiêu chí kiểm duyệt', 'Các nội dung cần đối chiếu trước khi quản trị viên phê duyệt hồ sơ lô hàng.', 'catalog-inspection-container');
      await group('ComplaintReason', 'Lý do khiếu nại', 'Danh mục lý do khiếu nại khả dụng cho các bên khi phát sinh tranh chấp.', 'catalog-complaint-container');
    }

    if (document.querySelector('#create-form')) {
      const input = document.querySelector('#create-form [name="reason"]');
      if (input) {
        const select = node('select');
        select.id = input.id;
        select.name = 'reasonId';
        select.required = true;
        const empty = node('option', 'Chọn lý do từ danh mục');
        empty.value = '';
        select.append(empty);
        for (const reason of await AgriApi.request('/api/v1/workflow-catalog?kind=ComplaintReason')) {
          const option = node('option', reason.name);
          option.value = reason.id;
          select.append(option);
        }
        input.replaceWith(select);
        const name = node('input');
        name.type = 'hidden';
        name.name = 'reason';
        select.addEventListener('change', () => { name.value = select.selectedOptions[0]?.textContent || ''; });
        select.after(name);
      }
    }
  } catch (error) {
    if (error.status !== 404) {
      const target = document.querySelector('#page-message');
      if (target) target.textContent = error.message;
    }
  }
})();
