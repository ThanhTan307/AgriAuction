(function () {
  'use strict';
  let csrfPromise;
  class ApiError extends Error {
    constructor(status, problem) {
      const fallback = status === 403 ? 'Bạn không có quyền thực hiện thao tác này.' : status === 401 ? 'Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.' : status === 429 ? 'Bạn thao tác quá nhanh. Vui lòng chờ một lát rồi thử lại.' : status >= 500 ? 'Hệ thống đang gặp sự cố. Vui lòng thử lại sau.' : 'Không thể hoàn tất yêu cầu. Vui lòng kiểm tra thông tin và thử lại.';
      const title = problem.title || '';
      super(status === 403 || status >= 500 || /HTTP|exception|stack trace|Failed to fetch|^One or more validation errors/i.test(title) ? fallback : title || fallback);
      this.status = status;
      this.problem = problem;
    }
  }
  async function csrf() {
    if (!csrfPromise) {
      csrfPromise = fetch('/api/v1/auth/csrf', { credentials: 'same-origin', cache: 'no-store' })
        .then(async response => {
          if (!response.ok) throw new ApiError(response.status, { title: 'Không thể khởi tạo phiên biểu mẫu.' });
          return (await response.json()).requestToken;
        }).catch(error => { csrfPromise = null; throw error; });
    }
    return csrfPromise;
  }
  async function request(path, options = {}) {
    const headers = new Headers(options.headers || {});
    headers.set('Accept', 'application/json');
    const method = (options.method || 'GET').toUpperCase();
    if (!['GET', 'HEAD'].includes(method)) headers.set('X-CSRF-TOKEN', await csrf());
    let body = options.body;
    if (body !== undefined && !(body instanceof FormData)) {
      headers.set('Content-Type', 'application/json');
      body = JSON.stringify(body);
    }
    let response;
    try {
      response = await fetch(path, { ...options, method, body, headers, credentials: 'same-origin', cache: 'no-store' });
    } catch (error) {
      if (error.name === 'AbortError') throw error;
      throw new ApiError(0, { title: 'Không thể kết nối đến hệ thống. Chưa xác nhận thao tác thành công; hãy tải lại dữ liệu trước khi thử lại.' });
    }
    if (response.status === 204) return null;
    const problem = await response.json().catch(() => ({ title: 'Không đọc được dữ liệu phản hồi. Vui lòng tải lại và thử lại.' }));
    if (!response.ok) throw new ApiError(response.status, problem);
    return problem;
  }
  window.AgriApi = { request, ApiError, resetCsrf: () => { csrfPromise = null; } };
})();
