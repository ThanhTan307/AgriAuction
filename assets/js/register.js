(function () {
  'use strict';
  const form = document.getElementById('register-form');
  const message = document.getElementById('register-message');
  const submit = document.getElementById('register-submit');
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (!form.reportValidity()) return;
    submit.disabled = true;
    message.textContent = 'Đang gửi đăng ký…';
    try {
      await AgriApi.request('/api/v1/auth/register', {
        method: 'POST',
        body: {
          email: form.elements.email.value,
          password: form.elements.password.value,
          displayName: form.elements.displayName.value,
          role: form.elements.role.value
        }
      });
      form.elements.password.value = '';
      message.textContent = 'Đã đăng ký. Đăng nhập để xem hồ sơ; Admin cần duyệt trước khi tạo lô hoặc tham gia phiên.';
    } catch (error) {
      message.textContent = error.message;
    } finally {
      submit.disabled = false;
    }
  });
})();
