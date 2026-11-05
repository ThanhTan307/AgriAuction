(function () {
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

  const states = {
    Draft: 'Bản nháp',
    PendingReview: 'Chờ kiểm duyệt',
    Approved: 'Đã phê duyệt',
    Rejected: 'Bị từ chối',
    Scheduled: 'Đã công bố',
    Prepared: 'Chưa bắt đầu',
    Open: 'Đang mở',
    ClosedWithWinner: 'Có người thắng',
    ClosedNoWinner: 'Không có người thắng',
    PendingDeposit: 'Chờ xác nhận cọc',
    SimulationEligible: 'Đã xác nhận cọc · Đủ điều kiện tham gia',
    AwaitingSimulation: 'Chờ xác nhận cọc',
    SimulatedHeld: 'Đã giữ cọc mô phỏng',
    SimulatedRefunded: 'Đã hoàn cọc mô phỏng',
    InProgress: 'Đang thực hiện',
    Completed: 'Đã hoàn tất',
    AwaitingRequest: 'Chờ yêu cầu thanh toán',
    AwaitingConfirmation: 'Chờ xác nhận thanh toán',
    SimulatedConfirmed: 'Đã thanh toán mô phỏng',
    AwaitingDelivery: 'Chờ giao hàng',
    Delivered: 'Đã giao hàng (chờ nhận)',
    Received: 'Đã nhận hàng',
    IssueReported: 'Có sự cố giao nhận',
    UnderReview: 'Đang xem xét',
    MediationPending: 'Chờ hòa giải',
    DecisionRecorded: 'Đã giải quyết',
    AwaitingPolicy: 'Chờ áp dụng chính sách',
    PolicyLocked: 'Chính sách chưa được xác nhận',
    SimulatedForfeited: 'Đã tịch thu cọc mô phỏng',
    Forfeited: 'Đã tịch thu cọc',
    Refunded: 'Đã hoàn cọc',
    Confirmed: 'Đã xác nhận',
    Registered: 'Đã đăng ký',
    PendingVerification: 'Chờ xác nhận cọc',
    Published: 'Đã công bố',
    OpenForRegistration: 'Đang nhận đăng ký',
    RegistrationClosed: 'Đã đóng đăng ký',
    Active: 'Đang diễn ra',
    Cancelled: 'Đã hủy',
    Enabled: 'Đang hoạt động',
    Disabled: 'Đã ngừng hoạt động',
    AuctionRecord: 'Phiên đấu giá',
    TransactionRecord: 'Hồ sơ giao dịch',
    WinnerRecorded: 'Đã ghi nhận người thắng',
    LotSoldSimulation: 'Lô hàng đã đấu giá',
    TransactionCreated: 'Đã tạo hồ sơ giao dịch',
    AuctionClosedNoWinner: 'Phiên kết thúc, không có người thắng',
    DepositSettlementRecorded: 'Đã quyết toán cọc',
    ComplaintCreated: 'Đã tiếp nhận khiếu nại',
    ComplaintResponded: 'Đã gửi giải trình',
    ComplaintStatusChanged: 'Đã cập nhật trạng thái khiếu nại',
    ComplaintCommented: 'Đã ghi nhận ý kiến',
    ComplaintDecisionRecorded: 'Đã giải quyết khiếu nại',
    TransactionActionRecorded: 'Đã cập nhật hồ sơ giao dịch',
    RequestPayment: 'Yêu cầu thanh toán',
    ConfirmPayment: 'Xác nhận thanh toán',
    Deliver: 'Xác nhận đã giao hàng',
    Receive: 'Xác nhận đã nhận hàng',
    Complete: 'Xác nhận hoàn tất giao dịch',
    ReportDeliveryIssue: 'Báo sự cố giao nhận',
    ResolveDeliveryIssue: 'Xử lý sự cố giao nhận',
    CreatedFromSimulationResult: 'Khởi tạo hồ sơ giao dịch',
    AtLeastStartingPrice: 'Ít nhất bằng giá khởi điểm',
    MinimumIncrease: 'Tăng ít nhất một bước giá',
    StartingPriceGrid: 'Theo bước giá từ giá khởi điểm',
    FirstCommittedWins: 'Ưu tiên lượt được ghi nhận trước',
    FromBidTime: 'Tính hạn từ thời điểm trả giá',
    AddToDeadline: 'Cộng thêm thời gian vào hạn hiện tại',
    AtOrAfterDeadline: 'Tự chốt khi đến hạn',
    Accept: 'Chấp nhận khiếu nại',
    Reject: 'Bác bỏ khiếu nại',
    Mediate: 'Đề nghị hòa giải',
    Loading: 'Đang tải',
    Connected: 'Đã kết nối',
    Unauthorized: 'Hết phiên đăng nhập'
  };
  const role = value => AppNavigation.roleName(value);
  const shortId = value => String(value || '').replace(/^([0-9a-f]{8})-[0-9a-f-]+$/i, '$1…');
  function reference(value) {
    const details = document.createElement('details'); details.className = 'p2-technical-reference';
    const summary = document.createElement('summary'); summary.textContent = 'Chi tiết kỹ thuật';
    const code = document.createElement('code'); code.textContent = String(value || '');
    const copy = document.createElement('button'); copy.type = 'button'; copy.className = 'p2-secondary'; copy.textContent = 'Sao chép mã'; copy.setAttribute('aria-label', 'Sao chép mã định danh');
    copy.addEventListener('click', async () => { try { await navigator.clipboard.writeText(String(value || '')); copy.textContent = 'Đã sao chép'; } catch { copy.textContent = 'Không thể sao chép'; } });
    details.append(summary, code, copy); return details;
  }
  function label(value) {
    if (value == null || value === '') return '—';
    if (typeof value === 'object') return '—';
    const key = String(value);
    if (states[key]) return states[key];
    return /^[A-Za-z][A-Za-z0-9_]*$/.test(key) ? 'Thông tin khác' : key;
  }
  function message(element, text, kind = '') { element.textContent = text; element.dataset.kind = kind; element.hidden = !text; }
  function error(element, failure) {
    if (failure.status === 401) {
      const returnUrl = encodeURIComponent(location.pathname + location.search);
      let modal = document.getElementById('agri-session-dialog');
      if (!modal) {
        modal = document.createElement('dialog');
        modal.id = 'agri-session-dialog';
        modal.className = 'p3-dialog';
        modal.innerHTML = `
          <h2>Phiên đăng nhập đã hết hạn</h2>
          <p class="p2-muted" style="margin:14px 0 20px;line-height:1.6">Phiên làm việc của bạn đã hết hạn hoặc bạn đã đăng nhập tài khoản khác ở tab khác. Vui lòng đăng nhập lại để tiếp tục thao tác.</p>
          <div class="p2-actions" style="justify-content:flex-end">
            <button type="button" class="p2-secondary" data-close-dialog>Đóng</button>
            <a href="/pages/login.html?ReturnUrl=${returnUrl}" class="p2-button">Đăng nhập lại</a>
          </div>
        `;
        document.body.append(modal);
        modal.querySelector('[data-close-dialog]')?.addEventListener('click', () => modal.close());
      }
      try { modal.showModal(); } catch { window.location.assign('/pages/login.html?ReturnUrl=' + returnUrl); }
      if (element) message(element, 'Phiên đăng nhập đã hết hạn. Vui lòng bấm Đăng nhập lại.', 'error');
      return;
    }
    const fields = Object.values(failure.problem?.errors || {}).flat().join(' ');
    const text = /Failed to fetch|NetworkError|Load failed/i.test(failure.message || '') ? 'Không thể kết nối đến hệ thống. Vui lòng kiểm tra mạng hoặc thử lại.' : failure.message || 'Không thể hoàn tất thao tác. Vui lòng tải lại và thử lại.';
    message(element, text + (fields ? '\n' + fields : '') + (failure.problem?.traceId ? '\nMã tra cứu: ' + failure.problem.traceId : ''), 'error');
  }
  function fieldErrors(root, failure) {
    root.querySelectorAll('[data-field-error]').forEach(node => { node.textContent = ''; });
    for (const [key, values] of Object.entries(failure?.problem?.errors || {})) {
      const field = key.split('.').pop();
      for (const node of root.querySelectorAll('[data-field-error]'))
        if (node.dataset.fieldError.toLowerCase() === field.toLowerCase()) node.textContent = values.join(' ');
    }
  }
  async function ready(role) { return AppNavigation.ready(role); }
  function badge(value) { const node = document.createElement('span'); node.className = 'p2-state'; node.dataset.state = value; node.textContent = label(value); return node; }
  function time(value) { if(!value)return '—';const date=new Date(value);return Number.isNaN(date.getTime())?'—':new Intl.DateTimeFormat('vi-VN',{timeZone:'Asia/Ho_Chi_Minh',dateStyle:'short',timeStyle:'short'}).format(date); }
  function decimal(value) {
    if (value === null || value === undefined || value === '') return '—';
    const [integer, fraction = ''] = String(value).split('.');
    const trimmed = fraction.replace(/0+$/, '');
    return integer.replace(/\B(?=(\d{3})+(?!\d))/g, '.') + (trimmed ? ',' + trimmed : '');
  }
  function parseDecimalInput(value) {
    let text = String(value ?? '').trim().replace(/\s|\u00a0/g, '');
    if (!text) return null;
    const comma = text.lastIndexOf(','), dot = text.lastIndexOf('.');
    if (comma >= 0 && dot >= 0) {
      const decimalSeparator = comma > dot ? ',' : '.';
      const thousandsSeparator = decimalSeparator === ',' ? /\./g : /,/g;
      text = text.replace(thousandsSeparator, '').replace(decimalSeparator, '.');
    } else if (comma >= 0) {
      text = text.replace(/\./g, '').replace(',', '.');
    } else if (dot >= 0) {
      const parts = text.split('.');
      text = parts.length === 2 && parts[1].length !== 3 ? text : text.replace(/\./g, '');
    }
    if (!/^(?:\d+|\d*\.\d+)$/.test(text)) return null;
    const number = Number(text);
    return Number.isFinite(number) ? number : null;
  }
  function priceBasis(value) {
    if (value == null || value === '') return '—';
    if (value === 'whole lot simulation') return 'Giá áp dụng cho toàn bộ lô hàng';
    if (value === 'per kilogram') return 'Giá áp dụng theo kg';
    return label(value);
  }
  function attributes(root, content, categoryName) {
    root.replaceChildren();
    for (const [label, value] of [ ['Loại nông sản', categoryName], ['Số lượng', decimal(content.quantity) + ' ' + content.quantityUnit],
      ['Khối lượng', decimal(content.weight) + ' ' + content.weightUnit], ['Địa điểm khai báo', content.location],
      ['Giá khởi điểm đề xuất', decimal(content.proposedStartingPrice) + ' ' + content.currency], ['Bước giá đề xuất', decimal(content.proposedIncrement) + ' ' + content.currency],
      ['Cơ sở tính giá đề xuất', priceBasis(content.priceBasis)], ['Bắt đầu đề xuất', time(content.proposedStartsAt)], ['Kết thúc đề xuất', time(content.proposedEndsAt)],
      ['Mô tả của nông dân', content.description], ['Thông tin chất lượng tự khai báo', content.qualityNotes || 'Chưa cung cấp'] ]) {
      const wrap = document.createElement('div'), dt = document.createElement('dt'), dd = document.createElement('dd'); dt.textContent = label; dd.textContent = value; wrap.append(dt, dd); root.append(wrap);
    }
  }
  function gallery(root, images, remove) {
    root.replaceChildren();
    if (!images.length) { const note = document.createElement('p'); note.className = 'p2-muted'; note.textContent = 'Chưa có ảnh gắn với lô.'; root.append(note); return; }
    for (const [index, item] of images.entries()) {
      const figure = document.createElement('figure'), img = document.createElement('img'), caption = document.createElement('figcaption');
      img.src = item.url; img.alt = 'Ảnh lô số ' + (index + 1); img.loading = 'lazy';
      img.addEventListener('error', () => { caption.textContent = 'Không tải được ảnh. Hãy kiểm tra phiên đăng nhập và tải lại.'; });
      img.tabIndex = 0; img.setAttribute('role', 'button'); img.setAttribute('aria-label', 'Phóng to ảnh lô số ' + (index + 1));
      const open = () => {
        const dialog = document.getElementById('photo-dialog'), preview = dialog.querySelector('img'); preview.src = item.url; preview.alt = img.alt; dialog.showModal();
      };
      img.addEventListener('click', open); img.addEventListener('keydown', event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); open(); } });
      caption.textContent = `${item.width} × ${item.height} px • Tải lên ${time(item.uploadedAt)}`;
      figure.append(img, caption);
      if (remove) { const button = document.createElement('button'); button.type = 'button'; button.className = 'p2-danger'; button.textContent = 'Gỡ ảnh ' + (index + 1); button.addEventListener('click', () => remove(item.id)); figure.append(button); }
      root.append(figure);
    }
  }
  function history(root, lot) {
    root.replaceChildren();
    if (!lot.revisions.length) { root.textContent = 'Chưa có lần gửi duyệt.'; return; }
    for (const revision of [...lot.revisions].reverse()) {
      const details = document.createElement('details'); details.className = 'p2-history-item';
      const summary = document.createElement('summary'); summary.textContent = `Lần gửi ${revision.number} • ${time(revision.submittedAt)} • ${revision.content.title}`; details.append(summary);
      const review = lot.reviews.find(x => x.revisionId === revision.id);
      const note = document.createElement('p'); note.textContent = review ? `${review.outcome === 'Approve' ? 'Phê duyệt' : 'Từ chối'} bởi ${review.adminName} lúc ${time(review.reviewedAt)}\n${review.reason}` : 'Chưa có quyết định.'; details.append(note);
      const fields = document.createElement('dl'); fields.className = 'p2-attributes'; attributes(fields, revision.content, lot.categoryName || 'Loại nông sản trong hồ sơ'); details.append(fields);
      const photos = document.createElement('div'); photos.className = 'p2-gallery'; gallery(photos, revision.images); details.append(photos); root.append(details);
    }
  }
  window.Phase2 = { ready, message, error, fieldErrors, badge, time, decimal, parseDecimalInput, priceBasis, attributes, gallery, history, states, label, role, shortId, reference };
})();
