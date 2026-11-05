import 'package:flutter/material.dart';

import '../../core/network/api_client.dart';
import '../auth/auth_state.dart';
import '../auth/account_pages.dart';

class ProfilePage extends StatefulWidget {
  const ProfilePage({super.key, required this.api, required this.authState});
  final ApiClient api;
  final AuthState authState;
  @override
  State<ProfilePage> createState() => _ProfilePageState();
}

class _ProfilePageState extends State<ProfilePage> {
  late Future<Map<String, dynamic>> _future = _load();

  Future<void> _edit(String currentName, int version) async {
    final name = await showDialog<String>(
      context: context,
      builder: (_) => _DisplayNameDialog(currentName: currentName),
    );
    if (name == null) return;
    try {
      await widget.api.patch('/api/v1/me/profile', {
        'displayName': name,
        'expectedVersion': version,
      });
      if (mounted) {
        setState(() {
          _future = _load();
        });
      }
    } catch (e) {
      if (mounted) {
        if (e is ApiException && e.statusCode == 409) {
          setState(() {
            _future = _load();
          });
        }
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text('$e')));
      }
    }
  }

  Future<Map<String, dynamic>> _load() async {
    final profile = Map<String, dynamic>.from(
      await widget.api.get('/api/v1/me/profile'),
    );
    final me = await widget.api.get('/api/v1/me');
    return {...profile, 'approvalState': me['approvalState']};
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<Map<String, dynamic>>(
    future: _future,
    builder: (context, snapshot) {
      if (snapshot.connectionState == ConnectionState.waiting && !snapshot.hasData) {
        return const Center(child: CircularProgressIndicator());
      }
      if (snapshot.hasError) {
        return Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Icons.error_outline, size: 48, color: Color(0xFFD97706)),
                const SizedBox(height: 12),
                Text('Không tải được hồ sơ: ${snapshot.error}'),
                const SizedBox(height: 12),
                FilledButton.icon(
                  onPressed: () => setState(() => _future = _load()),
                  icon: const Icon(Icons.refresh),
                  label: const Text('Thử lại'),
                ),
              ],
            ),
          ),
        );
      }
      final data = snapshot.data!;
      final displayName = data['displayName']?.toString() ?? '';
      final email = data['email']?.toString() ?? '';
      final role = const {
        'Administrator': 'Quản trị viên',
        'Enterprise': 'Doanh nghiệp',
        'Farmer': 'Nông dân',
      }[data['role']] ?? 'Nông dân';
      final approvalState = const {
        'Approved': 'Đã duyệt',
        'PendingReview': 'Chờ duyệt',
        'Rejected': 'Bị từ chối',
      }[data['approvalState']] ?? 'Đã xác minh';
      final version = int.tryParse(data['version']?.toString() ?? '1') ?? 1;

      return ListView(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
        children: [
          // User header card
          Container(
            padding: const EdgeInsets.all(18),
            decoration: BoxDecoration(
              color: Colors.white,
              borderRadius: BorderRadius.circular(12),
              border: Border.all(color: const Color(0xFFE2E8F0)),
            ),
            child: Row(
              children: [
                CircleAvatar(
                  radius: 30,
                  backgroundColor: const Color(0xFFDCFCE7),
                  child: Text(
                    (displayName.isNotEmpty ? displayName : '?')
                        .substring(0, 1)
                        .toUpperCase(),
                    style: const TextStyle(
                      fontSize: 22,
                      fontWeight: FontWeight.bold,
                      color: Color(0xFF15803D),
                    ),
                  ),
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        email.isNotEmpty ? email : 'Tài khoản Farmer',
                        style: const TextStyle(
                          fontSize: 15,
                          fontWeight: FontWeight.bold,
                          color: Color(0xFF0F172A),
                        ),
                      ),
                      const SizedBox(height: 3),
                      Text(
                        role,
                        style: const TextStyle(fontSize: 13, color: Color(0xFF64748B)),
                      ),
                      const SizedBox(height: 6),
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                        decoration: BoxDecoration(
                          color: const Color(0xFFDCFCE7),
                          borderRadius: BorderRadius.circular(6),
                        ),
                        child: Text(
                          approvalState,
                          style: const TextStyle(
                            fontSize: 11,
                            fontWeight: FontWeight.w600,
                            color: Color(0xFF166534),
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),

          const SizedBox(height: 16),

          // Detail info card
          Container(
            decoration: BoxDecoration(
              color: Colors.white,
              borderRadius: BorderRadius.circular(12),
              border: Border.all(color: const Color(0xFFE2E8F0)),
            ),
            child: Column(
              children: [
                _infoTile('Họ tên hiển thị', displayName),
                const Divider(height: 1, indent: 16, endIndent: 16, color: Color(0xFFF1F5F9)),
                _infoTile('Email đăng nhập', email),
                const Divider(height: 1, indent: 16, endIndent: 16, color: Color(0xFFF1F5F9)),
                _infoTile('Vai trò hệ thống', role),
                const Divider(height: 1, indent: 16, endIndent: 16, color: Color(0xFFF1F5F9)),
                _infoTile('Trạng thái tài khoản', approvalState),
              ],
            ),
          ),

          const SizedBox(height: 16),

          // Action buttons
          OutlinedButton.icon(
            onPressed: () => _edit(displayName, version),
            icon: const Icon(Icons.edit_outlined, size: 18),
            label: const Text('Sửa tên hiển thị'),
            style: OutlinedButton.styleFrom(
              minimumSize: const Size.fromHeight(46),
              side: const BorderSide(color: Color(0xFFCBD5E1)),
            ),
          ),
          const SizedBox(height: 10),
          OutlinedButton.icon(
            onPressed: () => Navigator.push(
              context,
              MaterialPageRoute<void>(
                builder: (_) =>
                    ChangePasswordPage(api: widget.api, auth: widget.authState),
              ),
            ),
            icon: const Icon(Icons.lock_outline, size: 18),
            label: const Text('Đổi mật khẩu'),
            style: OutlinedButton.styleFrom(
              minimumSize: const Size.fromHeight(46),
              side: const BorderSide(color: Color(0xFFCBD5E1)),
            ),
          ),
          const SizedBox(height: 10),
          OutlinedButton.icon(
            onPressed: () async {
              final confirm = await showDialog<bool>(
                context: context,
                builder: (ctx) => AlertDialog(
                  title: const Text('Đăng xuất'),
                  content: const Text('Bạn có chắc chắn muốn đăng xuất khỏi ứng dụng Farmer không?'),
                  actions: [
                    TextButton(
                      onPressed: () => Navigator.pop(ctx, false),
                      child: const Text('Hủy'),
                    ),
                    FilledButton(
                      style: FilledButton.styleFrom(backgroundColor: const Color(0xFFDC2626)),
                      onPressed: () => Navigator.pop(ctx, true),
                      child: const Text('Đăng xuất'),
                    ),
                  ],
                ),
              );
              if (confirm == true) {
                widget.authState.logout();
              }
            },
            icon: const Icon(Icons.logout, size: 18, color: Color(0xFFDC2626)),
            label: const Text('Đăng xuất', style: TextStyle(color: Color(0xFFDC2626))),
            style: OutlinedButton.styleFrom(
              minimumSize: const Size.fromHeight(46),
              side: const BorderSide(color: Color(0xFFFCA5A5)),
            ),
          ),
        ],
      );
    },
  );

  Widget _infoTile(String label, String value, {Widget? trailing}) => Padding(
    padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
    child: Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Text(label, style: const TextStyle(fontSize: 13, color: Color(0xFF64748B))),
        Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              value,
              style: const TextStyle(
                fontSize: 13,
                fontWeight: FontWeight.w600,
                color: Color(0xFF0F172A),
              ),
            ),
            if (trailing != null) ...[
              const SizedBox(width: 4),
              trailing,
            ],
          ],
        ),
      ],
    ),
  );
}

class _DisplayNameDialog extends StatefulWidget {
  const _DisplayNameDialog({required this.currentName});
  final String currentName;

  @override
  State<_DisplayNameDialog> createState() => _DisplayNameDialogState();
}

class _DisplayNameDialogState extends State<_DisplayNameDialog> {
  late final _input = TextEditingController(text: widget.currentName);

  @override
  void dispose() {
    _input.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Đổi họ tên hiển thị'),
    content: TextField(
      controller: _input,
      maxLength: 100,
      decoration: const InputDecoration(labelText: 'Họ tên mới'),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Hủy'),
      ),
      FilledButton(
        onPressed: () {
          if (_input.text.trim().isNotEmpty) {
            Navigator.pop(context, _input.text.trim());
          }
        },
        child: const Text('Lưu'),
      ),
    ],
  );
}
