import 'dart:io';
import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import '../../core/network/api_client.dart';
import '../../core/network/command_key.dart';
import '../../core/ui/formatters.dart';
import 'transaction_pages.dart';

class ComplaintsPage extends StatefulWidget {
  const ComplaintsPage({super.key, required this.api});
  final ApiClient api;
  @override
  State<ComplaintsPage> createState() => _ComplaintsState();
}

class _ComplaintsState extends State<ComplaintsPage> {
  int _page = 1;
  late Future<dynamic> _future = _load();
  Future<dynamic> _load() =>
      widget.api.get('/api/v1/complaints?page=$_page&pageSize=20');

  Widget _statusChip(String state) {
    Color bg = const Color(0xFFF1F5F9);
    Color fg = const Color(0xFF475569);
    String label = transactionText(state);

    switch (state) {
      case 'Submitted':
      case 'UnderReview':
        bg = const Color(0xFFFEF3C7);
        fg = const Color(0xFF92400E);
        break;
      case 'Resolved':
      case 'DecisionRecorded':
      case 'Closed':
        bg = const Color(0xFFDCFCE7);
        fg = const Color(0xFF166534);
        break;
      case 'Rejected':
      case 'Dismissed':
        bg = const Color(0xFFFEF2F2);
        fg = const Color(0xFFB91C1C);
        break;
    }

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: bg,
        borderRadius: BorderRadius.circular(6),
      ),
      child: Text(
        label,
        style: TextStyle(
          color: fg,
          fontSize: 11,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Khiếu nại & giải trình'),
      actions: [
        IconButton(
          onPressed: () => setState(() {
            _future = _load();
          }),
          icon: const Icon(Icons.refresh),
          tooltip: 'Tải lại',
        ),
      ],
    ),
    floatingActionButton: FloatingActionButton.extended(
      onPressed: () async {
        await Navigator.push(
          context,
          MaterialPageRoute<void>(
            builder: (_) => ComplaintCreatePage(api: widget.api),
          ),
        );
        if (mounted) {
          setState(() {
            _future = _load();
          });
        }
      },
      backgroundColor: const Color(0xFF15803D),
      foregroundColor: Colors.white,
      icon: const Icon(Icons.add),
      label: const Text(
        'Khiếu nại mới',
        style: TextStyle(fontWeight: FontWeight.w600),
      ),
    ),
    body: FutureBuilder<dynamic>(
      future: _future,
      builder: (context, s) {
        if (!s.hasData) {
          return Center(
            child: s.hasError
                ? Padding(
                    padding: const EdgeInsets.all(24),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        const Icon(Icons.error_outline, size: 48, color: Colors.amber),
                        const SizedBox(height: 12),
                        Text('Lỗi: ${s.error}'),
                        const SizedBox(height: 12),
                        FilledButton(
                          onPressed: () => setState(() {
                            _future = _load();
                          }),
                          child: const Text('Thử lại'),
                        ),
                      ],
                    ),
                  )
                : const CircularProgressIndicator(),
          );
        }
        final rows = s.data['items'] as List;
        final totalCount = int.tryParse(s.data['totalCount'].toString()) ?? 0;

        if (rows.isEmpty) {
          return Center(
            child: Padding(
              padding: const EdgeInsets.all(32),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Container(
                    width: 64,
                    height: 64,
                    decoration: const BoxDecoration(
                      color: Color(0xFFF1F5F9),
                      shape: BoxShape.circle,
                    ),
                    child: const Icon(
                      Icons.shield_outlined,
                      size: 32,
                      color: Color(0xFF94A3B8),
                    ),
                  ),
                  const SizedBox(height: 16),
                  const Text(
                    'Chưa có hồ sơ khiếu nại',
                    style: TextStyle(
                      fontSize: 16,
                      fontWeight: FontWeight.bold,
                      color: Color(0xFF0F172A),
                    ),
                  ),
                  const SizedBox(height: 6),
                  const Text(
                    'Các vấn đề tranh chấp, yêu cầu bồi hoàn hoặc giải trình giao dịch sẽ hiển thị tại đây.',
                    textAlign: TextAlign.center,
                    style: TextStyle(fontSize: 13, color: Color(0xFF64748B)),
                  ),
                ],
              ),
            ),
          );
        }

        return ListView(
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
          children: [
            for (final c in rows)
              Card(
                margin: const EdgeInsets.only(bottom: 12),
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(10),
                  side: const BorderSide(color: Color(0xFFE2E8F0)),
                ),
                elevation: 0,
                color: Colors.white,
                child: InkWell(
                  borderRadius: BorderRadius.circular(10),
                  onTap: () async {
                    await Navigator.push(
                      context,
                      MaterialPageRoute<void>(
                        builder: (_) => ComplaintDetailPage(
                          api: widget.api,
                          id: c['id'].toString(),
                        ),
                      ),
                    );
                    if (mounted) {
                      setState(() {
                        _future = _load();
                      });
                    }
                  },
                  child: Padding(
                    padding: const EdgeInsets.all(14),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Expanded(
                              child: Text(
                                c['title'].toString(),
                                style: const TextStyle(
                                  fontWeight: FontWeight.bold,
                                  fontSize: 14,
                                  color: Color(0xFF0F172A),
                                ),
                              ),
                            ),
                            const SizedBox(width: 8),
                            _statusChip(c['state'].toString()),
                          ],
                        ),
                        const SizedBox(height: 8),
                        Row(
                          children: [
                            Expanded(
                              child: Text(
                                'Lý do: ${c['reason'] ?? 'Không nêu'}',
                                style: const TextStyle(
                                  fontSize: 13,
                                  color: Color(0xFF475569),
                                ),
                              ),
                            ),
                            const Icon(
                              Icons.chevron_right,
                              size: 18,
                              color: Color(0xFF94A3B8),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            if (totalCount > 20) ...[
              const SizedBox(height: 12),
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  OutlinedButton.icon(
                    onPressed: _page > 1
                        ? () => setState(() {
                            _page--;
                            _future = _load();
                          })
                        : null,
                    icon: const Icon(Icons.chevron_left, size: 16),
                    label: const Text('Trước'),
                  ),
                  Text(
                    'Trang $_page',
                    style: const TextStyle(
                      fontWeight: FontWeight.w600,
                      color: Color(0xFF475569),
                    ),
                  ),
                  OutlinedButton.icon(
                    onPressed: _page * 20 < totalCount
                        ? () => setState(() {
                            _page++;
                            _future = _load();
                          })
                        : null,
                    label: const Text('Tiếp'),
                    icon: const Icon(Icons.chevron_right, size: 16),
                  ),
                ],
              ),
            ],
          ],
        );
      },
    ),
  );
}

class ComplaintCreatePage extends StatefulWidget {
  const ComplaintCreatePage({super.key, required this.api});
  final ApiClient api;
  @override
  State<ComplaintCreatePage> createState() => _ComplaintCreateState();
}

class _ComplaintCreateState extends State<ComplaintCreatePage> {
  final _text = TextEditingController();
  List<dynamic> _trades = [], _reasons = [];
  String? _target, _reason, _error, _key;
  String _scope = 'TransactionRecord';
  bool _loading = true, _busy = false;
  List<XFile> _images = [];
  Map<String, String>? _pendingFields;
  List<String>? _pendingFiles;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final trades = await widget.api.get('/api/v1/transactions?pageSize=100');
      final reasons = await widget.api.get(
        '/api/v1/workflow-catalog?kind=ComplaintReason',
      );
      if (mounted) {
        setState(() {
          _trades = (trades['items'] as List)
              .where((t) => t['completedAt'] != null)
              .toList();
          _reasons = reasons as List;
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = '$e');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  Future<void> _pick() async {
    final images = await ImagePicker().pickMultiImage();
    if (images.length > 3) {
      if (mounted) setState(() => _error = 'Chọn từ 1 đến 3 ảnh.');
      return;
    }
    if (images.isNotEmpty && mounted) setState(() => _images = images);
  }

  Future<void> _send() async {
    if (_key == null &&
        (_target == null ||
            _reason == null ||
            _text.text.trim().isEmpty ||
            _images.isEmpty)) {
      setState(
        () =>
            _error = 'Chọn giao dịch đã hoàn tất, lý do, nội dung và 1–3 ảnh.',
      );
      return;
    }
    _key ??= commandKey();
    _pendingFields ??= {
      'transactionId': _target!,
      'scope': _scope,
      'reasonId': _reason!,
      'reason': _reasons
          .firstWhere((r) => r['id'] == _reason)['name']
          .toString(),
      'description': _text.text.trim(),
    };
    _pendingFiles ??= _images.map((f) => f.path).toList();
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.api.multipart(
        '/api/v1/complaints',
        _pendingFields!,
        _pendingFiles!,
        headers: {'Idempotency-Key': _key!},
      );
      if (mounted) Navigator.pop(context);
    } on ApiException catch (e) {
      if (e.statusCode != 0 && e.statusCode < 500) {
        _key = null;
        _pendingFields = null;
        _pendingFiles = null;
      }
      if (mounted) setState(() => _error = e.message);
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Chưa xác nhận kết quả. Thử lại cùng yêu cầu.');
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final editable = !_busy && _key == null;
    return Scaffold(
      appBar: AppBar(title: const Text('Khiếu nại mô phỏng')),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : ListView(
              padding: const EdgeInsets.all(16),
              children: [
                Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: const Color(0xFFF0FDF4),
                    borderRadius: BorderRadius.circular(8),
                    border: Border.all(color: const Color(0xFFBBF7D0)),
                  ),
                  child: const Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Icon(Icons.shield_outlined, size: 20, color: Color(0xFF15803D)),
                      SizedBox(width: 10),
                      Expanded(
                        child: Text(
                          'Gửi khiếu nại trong 7 ngày sau khi giao dịch hoàn tất. Bên còn lại có 3 ngày để giải trình. Ảnh được bảo vệ trên hệ thống.',
                          style: TextStyle(fontSize: 13, color: Color(0xFF166534)),
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 16),
                DropdownButtonFormField<String>(
                  initialValue: _target,
                  isExpanded: true,
                  decoration: const InputDecoration(
                    labelText: 'Giao dịch đã hoàn tất',
                  ),
                  items: [
                    for (final t in _trades)
                      DropdownMenuItem(
                        value: t['id'].toString(),
                        child: Text(
                          t['title'].toString(),
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                  ],
                  onChanged: editable
                      ? (v) => setState(() => _target = v)
                      : null,
                ),
                const SizedBox(height: 16),
                DropdownButtonFormField<String>(
                  initialValue: _reason,
                  isExpanded: true,
                  decoration: const InputDecoration(
                    labelText: 'Lý do từ danh mục',
                  ),
                  items: [
                    for (final r in _reasons)
                      DropdownMenuItem(
                        value: r['id'].toString(),
                        child: Text(
                          r['name'].toString(),
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                  ],
                  onChanged: editable
                      ? (v) => setState(() => _reason = v)
                      : null,
                ),
                const SizedBox(height: 16),
                DropdownButtonFormField<String>(
                  initialValue: _scope,
                  decoration: const InputDecoration(
                    labelText: 'Nội dung liên quan',
                  ),
                  items: const [
                    DropdownMenuItem(
                      value: 'TransactionRecord',
                      child: Text('Giao dịch'),
                    ),
                    DropdownMenuItem(
                      value: 'AuctionRecord',
                      child: Text('Phiên đấu giá'),
                    ),
                  ],
                  onChanged: editable
                      ? (v) => setState(() => _scope = v!)
                      : null,
                ),
                const SizedBox(height: 16),
                TextField(
                  controller: _text,
                  enabled: editable,
                  maxLength: 4000,
                  maxLines: 4,
                  decoration: const InputDecoration(
                    labelText: 'Nội dung khiếu nại',
                  ),
                ),
                const SizedBox(height: 8),
                OutlinedButton.icon(
                  onPressed: editable ? _pick : null,
                  icon: const Icon(Icons.add_photo_alternate_outlined),
                  label: Text('Chọn ảnh minh chứng (${_images.length}/3)'),
                ),
                if (_images.isNotEmpty) ...[
                  const SizedBox(height: 12),
                  SizedBox(
                    height: 90,
                    child: ListView.separated(
                      scrollDirection: Axis.horizontal,
                      itemCount: _images.length,
                      separatorBuilder: (context, index) => const SizedBox(width: 8),
                      itemBuilder: (context, i) {
                        return Stack(
                          children: [
                            ClipRRect(
                              borderRadius: BorderRadius.circular(8),
                              child: Image.file(
                                File(_images[i].path),
                                width: 90,
                                height: 90,
                                fit: BoxFit.cover,
                              ),
                            ),
                            if (editable)
                              Positioned(
                                top: 2,
                                right: 2,
                                child: InkWell(
                                  onTap: () {
                                    setState(() {
                                      _images.removeAt(i);
                                    });
                                  },
                                  child: Container(
                                    decoration: const BoxDecoration(
                                      color: Colors.black54,
                                      shape: BoxShape.circle,
                                    ),
                                    padding: const EdgeInsets.all(4),
                                    child: const Icon(Icons.close, size: 14, color: Colors.white),
                                  ),
                                ),
                              ),
                          ],
                        );
                      },
                    ),
                  ),
                ],
                if (_error != null) ...[
                  const SizedBox(height: 12),
                  Container(
                    padding: const EdgeInsets.all(10),
                    decoration: BoxDecoration(
                      color: const Color(0xFFFEF2F2),
                      borderRadius: BorderRadius.circular(8),
                      border: Border.all(color: const Color(0xFFFECACA)),
                    ),
                    child: Row(
                      children: [
                        const Icon(Icons.error_outline, size: 18, color: Color(0xFFB91C1C)),
                        const SizedBox(width: 8),
                        Expanded(
                          child: Text(
                            _error!,
                            style: const TextStyle(fontSize: 12, color: Color(0xFFB91C1C)),
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
                          child: CircularProgressIndicator(
                            strokeWidth: 2,
                            color: Colors.white,
                          ),
                        )
                      : Text(
                          _key == null ? 'Gửi khiếu nại' : 'Thử lại cùng yêu cầu',
                          style: const TextStyle(fontWeight: FontWeight.bold),
                        ),
                ),
              ],
            ),
    );
  }
}

class ComplaintDetailPage extends StatefulWidget {
  const ComplaintDetailPage({super.key, required this.api, required this.id});
  final ApiClient api;
  final String id;
  @override
  State<ComplaintDetailPage> createState() => _ComplaintDetailState();
}

class _ComplaintDetailState extends State<ComplaintDetailPage> {
  late Future<dynamic> _future = _load();
  final _text = TextEditingController();
  List<XFile> _images = [];
  bool _busy = false;
  String? _error, _key;
  Map<String, String>? _fields;
  List<String>? _files;
  int _historyPage = 1;
  dynamic _extraHistory;
  Future<dynamic> _load() => widget.api.get('/api/v1/complaints/${widget.id}');

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  Future<void> _pick() async {
    final photos = await ImagePicker().pickMultiImage();
    if (photos.length > 3) {
      if (mounted) setState(() => _error = 'Chọn tối đa 3 ảnh.');
    } else if (photos.isNotEmpty && mounted) {
      setState(() => _images = photos);
    }
  }

  Future<void> _respond(dynamic c) async {
    if (_key == null && (_text.text.trim().isEmpty || _images.isEmpty)) {
      setState(() => _error = 'Cần nội dung và 1–3 ảnh giải trình.');
      return;
    }
    _key ??= commandKey();
    _fields ??= {
      'expectedVersion': c['version'].toString(),
      'text': _text.text.trim(),
    };
    _files ??= _images.map((p) => p.path).toList();
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.api.multipart(
        '/api/v1/complaints/${widget.id}/responses',
        _fields!,
        _files!,
        headers: {'Idempotency-Key': _key!},
      );
      _key = null;
      _fields = null;
      _files = null;
      _text.clear();
      _images = [];
      if (mounted) {
        setState(() {
          _future = _load();
          _extraHistory = null;
          _historyPage = 1;
        });
      }
    } on ApiException catch (e) {
      if (e.statusCode != 0 && e.statusCode < 500) {
        _key = null;
        _fields = null;
        _files = null;
      }
      if (mounted) setState(() => _error = e.message);
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Chưa xác nhận kết quả. Thử lại cùng yêu cầu.');
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _history(int page) async {
    try {
      final r = await widget.api.get(
        '/api/v1/complaints/${widget.id}/entries?page=$page&pageSize=50',
      );
      if (mounted) {
        setState(() {
          _historyPage = page;
          _extraHistory = r;
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = '$e');
    }
  }

  Widget _statusChip(String state) {
    Color bg = const Color(0xFFF1F5F9);
    Color fg = const Color(0xFF475569);
    String label = transactionText(state);

    switch (state) {
      case 'Submitted':
      case 'UnderReview':
        bg = const Color(0xFFFEF3C7);
        fg = const Color(0xFF92400E);
        break;
      case 'Resolved':
      case 'DecisionRecorded':
      case 'Closed':
        bg = const Color(0xFFDCFCE7);
        fg = const Color(0xFF166534);
        break;
      case 'Rejected':
      case 'Dismissed':
        bg = const Color(0xFFFEF2F2);
        fg = const Color(0xFFB91C1C);
        break;
    }

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: bg,
        borderRadius: BorderRadius.circular(6),
      ),
      child: Text(
        label,
        style: TextStyle(
          color: fg,
          fontSize: 11,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Hồ sơ khiếu nại'),
      actions: [
        IconButton(
          onPressed: () => setState(() {
            _future = _load();
          }),
          icon: const Icon(Icons.refresh),
          tooltip: 'Tải lại',
        ),
      ],
    ),
    body: FutureBuilder<dynamic>(
      future: _future,
      builder: (context, s) {
        if (!s.hasData) {
          return Center(
            child: s.hasError
                ? Text('${s.error}')
                : const CircularProgressIndicator(),
          );
        }
        final c = s.data['complaint'],
            history = _extraHistory ?? s.data['entries'];
        final totalHistoryCount =
            int.tryParse(history['totalCount'].toString()) ?? 0;

        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
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
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            c['title'].toString(),
                            style: const TextStyle(
                              fontSize: 16,
                              fontWeight: FontWeight.bold,
                              color: Color(0xFF0F172A),
                            ),
                          ),
                        ),
                        const SizedBox(width: 8),
                        _statusChip(c['state'].toString()),
                      ],
                    ),
                    const SizedBox(height: 8),
                    Text(
                      'Lý do: ${c['reason'] ?? 'Không nêu'}',
                      style: const TextStyle(
                        fontSize: 13,
                        color: Color(0xFF475569),
                        fontWeight: FontWeight.w500,
                      ),
                    ),
                    const SizedBox(height: 10),
                    Text(
                      c['description'].toString(),
                      style: const TextStyle(
                        fontSize: 14,
                        color: Color(0xFF1E293B),
                        height: 1.4,
                      ),
                    ),
                    if (c['responseDueAt'] != null) ...[
                      const SizedBox(height: 12),
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
                        decoration: BoxDecoration(
                          color: const Color(0xFFFFFBEB),
                          borderRadius: BorderRadius.circular(6),
                          border: Border.all(color: const Color(0xFFFDE68A)),
                        ),
                        child: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            const Icon(Icons.timer_outlined, size: 16, color: Color(0xFFB45309)),
                            const SizedBox(width: 6),
                            Text(
                              'Hạn giải trình: ${formatDateTime(c['responseDueAt'])}',
                              style: const TextStyle(
                                fontSize: 12,
                                fontWeight: FontWeight.w600,
                                color: Color(0xFFB45309),
                              ),
                            ),
                          ],
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ),
            const SizedBox(height: 20),
            const Text(
              'Diễn biến & giải trình',
              style: TextStyle(
                fontSize: 14,
                fontWeight: FontWeight.bold,
                color: Color(0xFF0F172A),
              ),
            ),
            const SizedBox(height: 8),
            for (final h in history['items'] as List)
              Card(
                margin: const EdgeInsets.only(bottom: 10),
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(10),
                  side: const BorderSide(color: Color(0xFFE2E8F0)),
                ),
                elevation: 0,
                color: Colors.white,
                child: Padding(
                  padding: const EdgeInsets.all(14),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Row(
                        mainAxisAlignment: MainAxisAlignment.spaceBetween,
                        children: [
                          Text(
                            transactionText(h['kind']),
                            style: const TextStyle(
                              fontSize: 13,
                              fontWeight: FontWeight.bold,
                              color: Color(0xFF15803D),
                            ),
                          ),
                          Text(
                            formatDateTime(h['occurredAt']),
                            style: const TextStyle(
                              fontSize: 12,
                              color: Color(0xFF94A3B8),
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 8),
                      Text(
                        h['text'].toString(),
                        style: const TextStyle(
                          fontSize: 13,
                          color: Color(0xFF334155),
                          height: 1.4,
                        ),
                      ),
                      if (h['evidence'] != null && (h['evidence'] as List).isNotEmpty) ...[
                        const SizedBox(height: 10),
                        Wrap(
                          spacing: 8,
                          runSpacing: 8,
                          children: [
                            for (final image in h['evidence'] as List)
                              ClipRRect(
                                borderRadius: BorderRadius.circular(6),
                                child: Image.network(
                                  '${widget.api.config.baseUrl}${image['url']}',
                                  headers: widget.api.authHeaders,
                                  width: 100,
                                  height: 80,
                                  fit: BoxFit.cover,
                                  errorBuilder: (_, _, _) => Container(
                                    width: 100,
                                    height: 80,
                                    color: const Color(0xFFF1F5F9),
                                    child: const Icon(
                                      Icons.broken_image,
                                      color: Color(0xFF94A3B8),
                                    ),
                                  ),
                                ),
                              ),
                          ],
                        ),
                      ],
                    ],
                  ),
                ),
              ),
            if (totalHistoryCount > 50) ...[
              const SizedBox(height: 8),
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  TextButton(
                    onPressed: _historyPage > 1
                        ? () => _history(_historyPage - 1)
                        : null,
                    child: const Text('Trước'),
                  ),
                  Text('Trang $_historyPage'),
                  TextButton(
                    onPressed: _historyPage * 50 < totalHistoryCount
                        ? () => _history(_historyPage + 1)
                        : null,
                    child: const Text('Tiếp'),
                  ),
                ],
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: 12),
              Container(
                padding: const EdgeInsets.all(10),
                decoration: BoxDecoration(
                  color: const Color(0xFFFEF2F2),
                  borderRadius: BorderRadius.circular(8),
                  border: Border.all(color: const Color(0xFFFECACA)),
                ),
                child: Row(
                  children: [
                    const Icon(Icons.error_outline, size: 18, color: Color(0xFFB91C1C)),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        _error!,
                        style: const TextStyle(fontSize: 12, color: Color(0xFFB91C1C)),
                      ),
                    ),
                  ],
                ),
              ),
            ],
            if (c['state'] != 'DecisionRecorded') ...[
              const SizedBox(height: 16),
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
                      const Text(
                        'Phản hồi & bổ sung chứng cứ',
                        style: TextStyle(
                          fontSize: 14,
                          fontWeight: FontWeight.bold,
                          color: Color(0xFF0F172A),
                        ),
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: _text,
                        enabled: !_busy && _key == null,
                        maxLength: 4000,
                        maxLines: 3,
                        decoration: const InputDecoration(
                          labelText: 'Nội dung giải trình',
                        ),
                      ),
                      const SizedBox(height: 8),
                      OutlinedButton.icon(
                        onPressed: !_busy && _key == null ? _pick : null,
                        icon: const Icon(Icons.add_photo_alternate_outlined),
                        label: Text('Chọn ảnh giải trình (${_images.length}/3)'),
                      ),
                      if (_images.isNotEmpty) ...[
                        const SizedBox(height: 10),
                        SizedBox(
                          height: 80,
                          child: ListView.separated(
                            scrollDirection: Axis.horizontal,
                            itemCount: _images.length,
                            separatorBuilder: (context, index) => const SizedBox(width: 8),
                            itemBuilder: (context, i) {
                              return ClipRRect(
                                borderRadius: BorderRadius.circular(6),
                                child: Image.file(
                                  File(_images[i].path),
                                  width: 80,
                                  height: 80,
                                  fit: BoxFit.cover,
                                ),
                              );
                            },
                          ),
                        ),
                      ],
                      const SizedBox(height: 16),
                      FilledButton(
                        onPressed: _busy ? null : () => _respond(c),
                        style: FilledButton.styleFrom(
                          minimumSize: const Size.fromHeight(48),
                          backgroundColor: const Color(0xFF15803D),
                        ),
                        child: _busy
                            ? const SizedBox(
                                width: 20,
                                height: 20,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                  color: Colors.white,
                                ),
                              )
                            : Text(
                                _key == null ? 'Gửi giải trình' : 'Thử lại cùng yêu cầu',
                                style: const TextStyle(fontWeight: FontWeight.bold),
                              ),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ],
        );
      },
    ),
  );
}
