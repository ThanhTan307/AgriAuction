import 'package:flutter/material.dart';
import '../../core/network/api_client.dart';
import 'auth_state.dart';

class RegisterFarmerPage extends StatefulWidget {
  const RegisterFarmerPage({super.key, required this.api});
  final ApiClient api;
  @override
  State<RegisterFarmerPage> createState() => _RegisterState();
}

class _RegisterState extends State<RegisterFarmerPage> {
  final _form = GlobalKey<FormState>(),
      _name = TextEditingController(),
      _email = TextEditingController(),
      _password = TextEditingController();
  bool _busy = false;
  String? _message;
  bool _isSuccess = false;

  @override
  void dispose() {
    _name.dispose();
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _send() async {
    if (!_form.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _message = null;
      _isSuccess = false;
    });
    try {
      await widget.api.post('/api/v1/auth/register', {
        'email': _email.text.trim(),
        'password': _password.text,
        'displayName': _name.text.trim(),
        'role': 'Farmer',
      });
      _password.clear();
      if (mounted) {
        setState(() {
          _isSuccess = true;
          _message =
              'Đăng ký thành công! Đăng nhập để xem hồ sơ. Quản trị viên sẽ phê duyệt tài khoản trước khi bạn có thể tạo và gửi lô nông sản.';
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _isSuccess = false;
          _message = '$e';
        });
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Đăng ký Farmer')),
    body: SafeArea(
      child: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(20),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 480),
            child: Form(
              key: _form,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Container(
                    padding: const EdgeInsets.all(14),
                    decoration: BoxDecoration(
                      color: const Color(0xFFF0FDF4),
                      borderRadius: BorderRadius.circular(10),
                      border: Border.all(color: const Color(0xFFBBF7D0)),
                    ),
                    child: const Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Icon(Icons.eco_outlined, size: 22, color: Color(0xFF15803D)),
                        SizedBox(width: 10),
                        Expanded(
                          child: Text(
                            'Tài khoản Farmer dành cho nông dân, hợp tác xã đưa nông sản lên sàn đấu giá minh bạch.',
                            style: TextStyle(fontSize: 13, color: Color(0xFF166534), height: 1.4),
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 20),
                  Card(
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(10),
                      side: const BorderSide(color: Color(0xFFE2E8F0)),
                    ),
                    elevation: 0,
                    color: Colors.white,
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          TextFormField(
                            controller: _name,
                            enabled: !_busy,
                            maxLength: 120,
                            decoration: const InputDecoration(labelText: 'Tên hiển thị / Tên nông hộ'),
                            validator: (v) =>
                                v == null || v.trim().isEmpty ? 'Nhập tên hiển thị.' : null,
                          ),
                          const SizedBox(height: 16),
                          TextFormField(
                            controller: _email,
                            enabled: !_busy,
                            keyboardType: TextInputType.emailAddress,
                            maxLength: 254,
                            decoration: const InputDecoration(labelText: 'Địa chỉ Email'),
                            validator: (v) =>
                                v == null || !v.contains('@') ? 'Nhập email hợp lệ.' : null,
                          ),
                          const SizedBox(height: 16),
                          TextFormField(
                            controller: _password,
                            enabled: !_busy,
                            obscureText: true,
                            autofillHints: const [AutofillHints.newPassword],
                            maxLength: 1024,
                            decoration: const InputDecoration(
                              labelText: 'Mật khẩu (ít nhất 8 ký tự)',
                            ),
                            validator: (v) =>
                                v == null || v.length < 8 ? 'Mật khẩu ít nhất 8 ký tự.' : null,
                          ),
                        ],
                      ),
                    ),
                  ),
                  if (_message != null) ...[
                    const SizedBox(height: 14),
                    Container(
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        color: _isSuccess ? const Color(0xFFDCFCE7) : const Color(0xFFFEF2F2),
                        borderRadius: BorderRadius.circular(8),
                        border: Border.all(
                          color: _isSuccess ? const Color(0xFF86EFAC) : const Color(0xFFFECACA),
                        ),
                      ),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Icon(
                            _isSuccess ? Icons.check_circle_outline : Icons.error_outline,
                            size: 18,
                            color: _isSuccess ? const Color(0xFF166534) : const Color(0xFFB91C1C),
                          ),
                          const SizedBox(width: 8),
                          Expanded(
                            child: Text(
                              _message!,
                              style: TextStyle(
                                fontSize: 13,
                                color: _isSuccess ? const Color(0xFF166534) : const Color(0xFFB91C1C),
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],
                  const SizedBox(height: 24),
                  FilledButton(
                    onPressed: _busy ? null : _send,
                    style: FilledButton.styleFrom(
                      minimumSize: const Size.fromHeight(48),
                      backgroundColor: const Color(0xFF15803D),
                    ),
                    child: _busy
                        ? const SizedBox(
                            width: 20,
                            height: 20,
                            child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                          )
                        : const Text(
                            'Gửi đăng ký',
                            style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15),
                          ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    ),
  );
}

class ChangePasswordPage extends StatefulWidget {
  const ChangePasswordPage({super.key, required this.api, required this.auth});
  final ApiClient api;
  final AuthState auth;
  @override
  State<ChangePasswordPage> createState() => _ChangePasswordState();
}

class _ChangePasswordState extends State<ChangePasswordPage> {
  final _form = GlobalKey<FormState>(),
      _old = TextEditingController(),
      _next = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _old.dispose();
    _next.dispose();
    super.dispose();
  }

  Future<void> _send() async {
    if (!_form.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.api.post('/api/v1/me/change-password', {
        'currentPassword': _old.text,
        'newPassword': _next.text,
      });
      _old.clear();
      _next.clear();
      await widget.auth.logout();
      if (mounted) Navigator.pop(context);
    } catch (e) {
      if (mounted) setState(() => _error = '$e');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Đổi mật khẩu')),
    body: SafeArea(
      child: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(20),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 480),
            child: Form(
              key: _form,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Container(
                    padding: const EdgeInsets.all(14),
                    decoration: BoxDecoration(
                      color: const Color(0xFFFFFBEB),
                      borderRadius: BorderRadius.circular(10),
                      border: Border.all(color: const Color(0xFFFDE68A)),
                    ),
                    child: const Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Icon(Icons.lock_reset, size: 22, color: Color(0xFFB45309)),
                        SizedBox(width: 10),
                        Expanded(
                          child: Text(
                            'Sau khi đổi mật khẩu, toàn bộ phiên đăng nhập hiện tại trên các thiết bị sẽ kết thúc. Bạn cần đăng nhập lại với mật khẩu mới.',
                            style: TextStyle(fontSize: 13, color: Color(0xFF92400E), height: 1.4),
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 20),
                  Card(
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(10),
                      side: const BorderSide(color: Color(0xFFE2E8F0)),
                    ),
                    elevation: 0,
                    color: Colors.white,
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          TextFormField(
                            controller: _old,
                            enabled: !_busy,
                            obscureText: true,
                            autofillHints: const [AutofillHints.password],
                            decoration: const InputDecoration(labelText: 'Mật khẩu hiện tại'),
                            validator: (v) =>
                                v == null || v.isEmpty ? 'Nhập mật khẩu hiện tại.' : null,
                          ),
                          const SizedBox(height: 16),
                          TextFormField(
                            controller: _next,
                            enabled: !_busy,
                            obscureText: true,
                            maxLength: 1024,
                            autofillHints: const [AutofillHints.newPassword],
                            decoration: const InputDecoration(
                              labelText: 'Mật khẩu mới (ít nhất 8 ký tự)',
                            ),
                            validator: (v) =>
                                v == null || v.length < 8 ? 'Mật khẩu ít nhất 8 ký tự.' : null,
                          ),
                        ],
                      ),
                    ),
                  ),
                  if (_error != null) ...[
                    const SizedBox(height: 14),
                    Container(
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        color: const Color(0xFFFEF2F2),
                        borderRadius: BorderRadius.circular(8),
                        border: Border.all(color: const Color(0xFFFECACA)),
                      ),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          const Icon(Icons.error_outline, size: 18, color: Color(0xFFB91C1C)),
                          const SizedBox(width: 8),
                          Expanded(
                            child: Text(
                              _error!,
                              style: const TextStyle(fontSize: 13, color: Color(0xFFB91C1C)),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],
                  const SizedBox(height: 24),
                  FilledButton(
                    onPressed: _busy ? null : _send,
                    style: FilledButton.styleFrom(
                      minimumSize: const Size.fromHeight(48),
                      backgroundColor: const Color(0xFF15803D),
                    ),
                    child: _busy
                        ? const SizedBox(
                            width: 20,
                            height: 20,
                            child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                          )
                        : const Text(
                            'Đổi mật khẩu',
                            style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15),
                          ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    ),
  );
}
