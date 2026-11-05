import 'package:flutter/material.dart';
import '../../core/network/api_client.dart';
import '../../core/network/command_key.dart';
import '../../core/network/live_updates.dart';
import '../../core/ui/formatters.dart';

const transactionLabels = {
  'AwaitingPolicy': 'Chờ áp dụng chính sách',
  'InProgress': 'Đang thực hiện',
  'Completed': 'Đã hoàn tất',
  'Draft': 'Bản nháp',
  'Scheduled': 'Đã công bố',
  'Prepared': 'Đã cấu hình · Chưa bắt đầu',
  'ClosedWithWinner': 'Đã chốt · Có người thắng',
  'ClosedNoWinner': 'Đã chốt · Không có lượt trả giá',
  'AwaitingRequest': 'Chờ yêu cầu thanh toán',
  'AwaitingConfirmation': 'Chờ xác nhận thanh toán',
  'SimulatedConfirmed': 'Đã xác nhận thanh toán',
  'AwaitingDelivery': 'Chờ giao hàng',
  'Delivered': 'Farmer đã xác nhận giao',
  'Received': 'Đã nhận hàng',
  'IssueReported': 'Có sự cố giao nhận · chờ xử lý',
  'PolicyLocked': 'Đang khóa chính sách',
  'Deliver': 'Xác nhận đã giao',
  'Receive': 'Xác nhận đã nhận hàng',
  'Complete': 'Xác nhận hoàn tất giao dịch',
  'ReportDeliveryIssue': 'Báo sự cố giao nhận',
  'RequestPayment': 'Yêu cầu thanh toán mô phỏng',
  'ConfirmPayment': 'Xác nhận thanh toán mô phỏng',
  'CreatedFromSimulationResult': 'Khởi tạo hồ sơ giao dịch',
  'ResolveDeliveryIssue': 'Xử lý sự cố giao nhận',
  'DisputeOpened': 'Mở tranh chấp',
  'DisputeDecisionRecorded': 'Ghi nhận quyết định tranh chấp',
  'SimulatedHeld': 'Đã giữ cọc mô phỏng',
  'AwaitingSimulation': 'Chờ xác nhận cọc',
  'SimulatedRefunded': 'Đã hoàn cọc mô phỏng',
  'SimulatedForfeited': 'Đã tịch thu cọc mô phỏng',
  'PendingDeposit': 'Chờ xác nhận cọc',
  'SimulationEligible': 'Đã xác nhận cọc · Đủ điều kiện tham gia',
  'Approved': 'Đã phê duyệt',
  'Rejected': 'Bị từ chối',
  'DecisionRecorded': 'Đã giải quyết',
  'Open': 'Đang mở',
  'UnderReview': 'Đang xem xét',
  'MediationPending': 'Chờ hòa giải',
  'Forfeited': 'Đã tịch thu cọc',
  'Refund': 'Hoàn cọc mô phỏng',
  'Forfeit': 'Tịch thu cọc mô phỏng',
  'ComplaintCreated': 'Đã tiếp nhận khiếu nại',
  'ComplaintResponded': 'Đã gửi giải trình',
  'ComplaintStatusChanged': 'Đã cập nhật trạng thái khiếu nại',
  'ComplaintCommented': 'Đã ghi nhận ý kiến',
  'ComplaintDecisionRecorded': 'Đã giải quyết khiếu nại',
  'Created': 'Đã gửi khiếu nại',
  'ResponseAdded': 'Đã gửi giải trình bổ sung',
  'ReviewStarted': 'Đã bắt đầu xem xét',
  'AdminComment': 'Quản trị viên đã nhận xét',
  'TransactionActionRecorded': 'Đã cập nhật hồ sơ giao dịch',
  'WinnerRecorded': 'Đã ghi nhận người thắng',
  'LotSoldSimulation': 'Lô hàng đã đấu giá',
  'TransactionCreated': 'Đã tạo hồ sơ giao dịch',
  'AuctionClosedNoWinner': 'Phiên kết thúc, không có người thắng',
  'DepositSettlementRecorded': 'Đã quyết toán cọc',
};

String transactionText(dynamic value) {
  if (value == null || value.toString().isEmpty) return '—';
  if (value is Map || value is List) return '—';
  final text = value.toString();
  return transactionLabels[text] ??
      (RegExp(r'^[A-Za-z][A-Za-z0-9_]*$').hasMatch(text) ? 'Thông tin khác' : text);
}

String _translateTechnicalTerms(String text) {
  text = text.replaceAll('Simulation', 'mô phỏng');
  for (final entry in transactionLabels.entries) {
    text = text.replaceAll(RegExp(r'\b${RegExp.escape(entry.key)}\b'), entry.value);
  }
  return text;
}

String notificationMessage(dynamic value) {
  if (value == null) return '';
  var text = value.toString();
  final refund = RegExp(r'^(Refund|SimulatedRefunded):\s*([\d.,]+)\s*VND\.?([\s\S]*)$', caseSensitive: false).firstMatch(text);
  if (refund != null) {
    var raw = refund.group(2)!;
    if (raw.contains(',') && raw.contains('.')) {
      raw = raw.lastIndexOf(',') > raw.lastIndexOf('.')
          ? raw.replaceAll('.', '').replaceFirst(',', '.')
          : raw.replaceAll(',', '');
    } else if (raw.contains(',')) {
      raw = raw.replaceFirst(',', '.');
    }
    final amount = double.tryParse(raw);
    if (amount != null && amount.isFinite) {
      final grouped = amount.round().toString().replaceAllMapped(RegExp(r'(\d)(?=(\d{3})+$)'), (m) => '${m[1]}.');
      final explanation = refund.group(3)!.trim();
      return 'Đã hoàn cọc mô phỏng: $grouped VND.${explanation.isEmpty ? '' : ' ${_translateTechnicalTerms(explanation)}'}';
    }
  }
  return _translateTechnicalTerms(text);
}

class TransactionsPage extends StatefulWidget {
  const TransactionsPage({super.key, required this.api});
  final ApiClient api;
  @override
  State<TransactionsPage> createState() => _TransactionsPageState();
}

class _TransactionsPageState extends State<TransactionsPage> {
  int _page = 1;
  late Future<dynamic> _future = _load();
  LiveUpdates? _live;

  @override
  void initState() {
    super.initState();
    _live = LiveUpdates(
      widget.api,
      '/hubs/notifications',
      'InboxChanged',
      () {
        if (mounted) {
          setState(() {
            _future = _load();
          });
        }
      },
    );
    _live?.connect();
  }

  @override
  void dispose() {
    _live?.dispose();
    super.dispose();
  }

  Future<dynamic> _load() =>
      widget.api.get('/api/v1/transactions?page=$_page&pageSize=20');

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Giao dịch mô phỏng'),
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
        if (s.connectionState == ConnectionState.waiting && !s.hasData) {
          return const Center(child: CircularProgressIndicator());
        }
        if (s.hasError) {
          return Center(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Icon(Icons.error_outline, size: 48, color: Colors.amber),
                  const SizedBox(height: 12),
                  Text('Không tải được giao dịch: ${s.error}'),
                  const SizedBox(height: 12),
                  FilledButton(
                    onPressed: () => setState(() {
                      _future = _load();
                    }),
                    child: const Text('Thử lại'),
                  ),
                ],
              ),
            ),
          );
        }
        final rows = s.data['items'] as List;
        final total = int.parse(s.data['totalCount'].toString());
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
              margin: const EdgeInsets.only(bottom: 14),
              decoration: BoxDecoration(
                color: const Color(0xFFECFDF5),
                borderRadius: BorderRadius.circular(8),
                border: Border.all(color: const Color(0xFFA7F3D0)),
              ),
              child: const Row(
                children: [
                  Icon(Icons.info_outline, size: 20, color: Color(0xFF065F46)),
                  SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      'Quy trình giao dịch mô phỏng · Không phát sinh tiền thật',
                      style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w600,
                        color: Color(0xFF065F46),
                      ),
                    ),
                  ),
                ],
              ),
            ),
            for (final t in rows)
              Card(
                margin: const EdgeInsets.only(bottom: 12),
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(10),
                  side: const BorderSide(color: Color(0xFFE2E8F0)),
                ),
                child: ListTile(
                  contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                  title: Text(
                    t['title'].toString(),
                    style: const TextStyle(fontWeight: FontWeight.bold),
                  ),
                  subtitle: Padding(
                    padding: const EdgeInsets.only(top: 6),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Trạng thái: ${transactionText(t['state'])}',
                          style: TextStyle(
                            color: t['state'] == 'Completed'
                                ? const Color(0xFF166534)
                                : const Color(0xFFB45309),
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                        const SizedBox(height: 4),
                        Text(
                          'Giá trúng: ${formatVnd(t['winningPrice'], t['currency']?.toString())} · ${displayPriceBasis(t['priceBasis'])}',
                          style: const TextStyle(color: Color(0xFF334155)),
                        ),
                      ],
                    ),
                  ),
                  trailing: const Icon(Icons.chevron_right, color: Color(0xFF94A3B8)),
                  onTap: () async {
                    await Navigator.push(
                      context,
                      MaterialPageRoute<void>(
                        builder: (_) => TransactionDetailPage(
                          api: widget.api,
                          id: t['id'].toString(),
                        ),
                      ),
                    );
                    if (mounted) {
                      setState(() {
                        _future = _load();
                      });
                    }
                  },
                ),
              ),
            if (rows.isEmpty)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: 40),
                child: Center(
                  child: Text(
                    'Chưa có hồ sơ giao dịch nào.',
                    style: TextStyle(color: Color(0xFF64748B)),
                  ),
                ),
              ),
            if (total > 0)
              Padding(
                padding: const EdgeInsets.only(top: 12),
                child: Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    TextButton.icon(
                      onPressed: _page > 1
                          ? () => setState(() {
                              _page--;
                              _future = _load();
                            })
                          : null,
                      icon: const Icon(Icons.chevron_left),
                      label: const Text('Trước'),
                    ),
                    Text(
                      'Trang $_page / ${((total - 1) ~/ 20) + 1} ($total bản ghi)',
                      style: const TextStyle(fontSize: 12, color: Color(0xFF64748B)),
                    ),
                    TextButton.icon(
                      onPressed: _page * 20 < total
                          ? () => setState(() {
                              _page++;
                              _future = _load();
                            })
                          : null,
                      icon: const Icon(Icons.chevron_right),
                      label: const Text('Tiếp'),
                    ),
                  ],
                ),
              ),
          ],
        );
      },
    ),
  );
}

class TransactionDetailPage extends StatefulWidget {
  const TransactionDetailPage({super.key, required this.api, required this.id});
  final ApiClient api;
  final String id;
  @override
  State<TransactionDetailPage> createState() => _TransactionDetailState();
}

class _TransactionDetailState extends State<TransactionDetailPage> {
  late Future<dynamic> _future = _load();
  LiveUpdates? _live;
  bool _busy = false;
  String? _error, _pendingAction, _pendingKey;
  Map<String, dynamic>? _pendingBody;

  @override
  void initState() {
    super.initState();
    _live = LiveUpdates(
      widget.api,
      '/hubs/notifications',
      'InboxChanged',
      () {
        if (mounted && !_busy) {
          setState(() {
            _future = _load();
          });
        }
      },
    );
    _live?.connect();
  }

  @override
  void dispose() {
    _live?.dispose();
    super.dispose();
  }

  Future<dynamic> _load() =>
      widget.api.get('/api/v1/transactions/${widget.id}');

  Future<void> _act(String action, Map<String, dynamic> t) async {
    String? note;
    if (_pendingKey == null && action == 'ReportDeliveryIssue') {
      final input = TextEditingController();
      note = await showDialog<String>(
        context: context,
        builder: (c) => AlertDialog(
          title: const Text('Báo sự cố giao nhận'),
          content: TextField(
            controller: input,
            maxLength: 1000,
            maxLines: 3,
            decoration: const InputDecoration(
              labelText: 'Lý do sự cố giao nhận (bắt buộc)',
              hintText: 'Nhập chi tiết sự cố hàng hóa hoặc vận chuyển…',
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(c),
              child: const Text('Hủy'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(c, input.text.trim()),
              child: const Text('Gửi báo cáo'),
            ),
          ],
        ),
      );
      input.dispose();
      if (note == null || note.isEmpty) return;
    }
    if (!mounted) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    _pendingAction ??= action;
    _pendingKey ??= commandKey();
    _pendingBody ??= {
      'expectedVersion': int.parse(t['version'].toString()),
      'note': note,
    };
    try {
      await widget.api.post(
        '/api/v1/transactions/${widget.id}/actions/$_pendingAction',
        _pendingBody,
        {'Idempotency-Key': _pendingKey!},
      );
      _pendingAction = null;
      _pendingKey = null;
      _pendingBody = null;
      if (mounted) {
        setState(() {
          _future = _load();
        });
      }
    } on ApiException catch (e) {
      if (e.statusCode != 0 && e.statusCode < 500) {
        _pendingAction = null;
        _pendingKey = null;
        _pendingBody = null;
      }
      if (mounted) {
        setState(() {
          _error = e.message;
          if (e.statusCode == 409) _future = _load();
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Chưa xác nhận kết quả. Thử lại cùng yêu cầu.');
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Widget _buildStepIndicator(Map<String, dynamic> t) {
    final paymentState = t['paymentState']?.toString() ?? '';
    final deliveryState = t['deliveryState']?.toString() ?? '';
    final isCompleted = t['state'] == 'Completed';

    final paymentDone = paymentState == 'SimulatedConfirmed';
    final deliveredDone = deliveryState == 'Delivered' || deliveryState == 'Received';
    final receivedDone = deliveryState == 'Received';

    return Container(
      padding: const EdgeInsets.all(14),
      margin: const EdgeInsets.only(bottom: 16),
      decoration: BoxDecoration(
        color: const Color(0xFFF8FAFC),
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: const Color(0xFFE2E8F0)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'Tiến trình giao dịch mô phỏng',
            style: TextStyle(fontSize: 13, fontWeight: FontWeight.bold, color: Color(0xFF0F172A)),
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              _stepItem('Thanh toán', paymentDone, paymentState == 'AwaitingConfirmation' || paymentState == 'AwaitingRequest'),
              _stepDivider(paymentDone),
              _stepItem('Giao hàng', deliveredDone, deliveryState == 'AwaitingDelivery'),
              _stepDivider(deliveredDone),
              _stepItem('Nhận hàng', receivedDone, deliveryState == 'Delivered'),
              _stepDivider(receivedDone),
              _stepItem('Hoàn tất', isCompleted, false),
            ],
          ),
        ],
      ),
    );
  }

  Widget _stepItem(String label, bool isDone, bool isActive) {
    final color = isDone
        ? const Color(0xFF15803D)
        : isActive
            ? const Color(0xFFD97706)
            : const Color(0xFF94A3B8);
    return Expanded(
      child: Column(
        children: [
          Container(
            width: 26,
            height: 26,
            decoration: BoxDecoration(
              color: isDone ? const Color(0xFF15803D) : isActive ? const Color(0xFFFEF3C7) : Colors.white,
              shape: BoxShape.circle,
              border: Border.all(color: color, width: 2),
            ),
            child: Center(
              child: isDone
                  ? const Icon(Icons.check, size: 16, color: Colors.white)
                  : Text(
                      isActive ? '•' : '',
                      style: TextStyle(color: color, fontWeight: FontWeight.bold),
                    ),
            ),
          ),
          const SizedBox(height: 4),
          Text(
            label,
            textAlign: TextAlign.center,
            style: TextStyle(
              fontSize: 10,
              fontWeight: isDone || isActive ? FontWeight.bold : FontWeight.normal,
              color: color,
            ),
          ),
        ],
      ),
    );
  }

  Widget _stepDivider(bool isDone) {
    return Container(
      width: 14,
      height: 2,
      color: isDone ? const Color(0xFF15803D) : const Color(0xFFCBD5E1),
      margin: const EdgeInsets.only(bottom: 16),
    );
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Chi tiết giao dịch'),
      actions: [
        IconButton(
          onPressed: _busy
              ? null
              : () => setState(() {
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
                ? Text('Lỗi tải dữ liệu: ${s.error}')
                : const CircularProgressIndicator(),
          );
        }
        final t = Map<String, dynamic>.from(s.data['transaction'] as Map);
        final actions = List<String>.from(s.data['allowedActions'] ?? []);
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Text(
              t['title'].toString(),
              style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                    fontWeight: FontWeight.bold,
                    color: const Color(0xFF0F172A),
                  ),
            ),
            const SizedBox(height: 6),
            const Text(
              'Giá trúng áp dụng cho toàn bộ lô hàng · Không phát sinh tiền thật',
              style: TextStyle(fontSize: 12, color: Color(0xFF065F46), fontWeight: FontWeight.w500),
            ),
            const SizedBox(height: 16),
            _buildStepIndicator(t),
            Card(
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(10),
                side: const BorderSide(color: Color(0xFFE2E8F0)),
              ),
              child: Padding(
                padding: const EdgeInsets.all(14),
                child: Column(
                  children: [
                    _infoRow('Giá trúng đấu giá', formatVnd(t['winningPrice'], t['currency']?.toString())),
                    _infoRow('Cơ sở tính giá', displayPriceBasis(t['priceBasis'])),
                    _infoRow('Trạng thái tổng thể', transactionText(t['state'])),
                    _infoRow('Thanh toán', transactionText(t['paymentState'])),
                    _infoRow('Giao nhận hàng', transactionText(t['deliveryState'])),
                    if (t['completedAt'] != null)
                      _infoRow('Thời điểm hoàn tất', formatDateTime(t['completedAt'])),
                    if (t['disputeState'] != null)
                      _infoRow('Tranh chấp', t['disputeState'].toString()),
                  ],
                ),
              ),
            ),
            if (_error != null) ...[
              const SizedBox(height: 12),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: const Color(0xFFFEF2F2),
                  borderRadius: BorderRadius.circular(8),
                  border: Border.all(color: const Color(0xFFFECACA)),
                ),
                child: Text(
                  _error!,
                  style: const TextStyle(color: Color(0xFFB91C1C), fontSize: 13),
                ),
              ),
            ],
            const SizedBox(height: 16),
            if (_pendingAction != null)
              FilledButton.icon(
                onPressed: _busy ? null : () => _act(_pendingAction!, t),
                icon: const Icon(Icons.replay),
                label: const Text('Thử lại thao tác chưa xác nhận'),
                style: FilledButton.styleFrom(
                  minimumSize: const Size.fromHeight(48),
                  backgroundColor: const Color(0xFFD97706),
                ),
              ),
            if (_pendingAction == null && actions.isNotEmpty) ...[
              const Text(
                'Thao tác khả dụng:',
                style: TextStyle(fontWeight: FontWeight.bold, fontSize: 13),
              ),
              const SizedBox(height: 8),
              for (final action in actions)
                Padding(
                  padding: const EdgeInsets.only(bottom: 8),
                  child: FilledButton(
                    onPressed: _busy ? null : () => _act(action, t),
                    style: FilledButton.styleFrom(
                      minimumSize: const Size.fromHeight(46),
                      backgroundColor: action == 'ReportDeliveryIssue'
                          ? const Color(0xFFDC2626)
                          : const Color(0xFF15803D),
                    ),
                    child: Text(transactionText(action)),
                  ),
                ),
            ],
            const SizedBox(height: 16),
            const Divider(),
            const SizedBox(height: 8),
            const Text(
              'Lịch sử xử lý hồ sơ',
              style: TextStyle(fontWeight: FontWeight.bold, fontSize: 14),
            ),
            const SizedBox(height: 8),
            for (final h in (s.data['history'] as List? ?? []))
              Card(
                margin: const EdgeInsets.only(bottom: 8),
                color: const Color(0xFFF8FAFC),
                elevation: 0,
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(8),
                  side: const BorderSide(color: Color(0xFFE2E8F0)),
                ),
                child: ListTile(
                  dense: true,
                  title: Text(
                    transactionText(h['kind']),
                    style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13),
                  ),
                  subtitle: Text(
                    '${formatDateTime(h['occurredAt'])}${h['note'] != null && h['note'].toString().isNotEmpty ? '\nGhi chú: ${h['note']}' : ''}',
                    style: const TextStyle(fontSize: 12, color: Color(0xFF64748B)),
                  ),
                ),
              ),
          ],
        );
      },
    ),
  );

  Widget _infoRow(String label, String value) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(label, style: const TextStyle(color: Color(0xFF64748B), fontSize: 13)),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              value,
              textAlign: TextAlign.right,
              style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13, color: Color(0xFF0F172A)),
            ),
          ),
        ],
      ),
    );
  }
}
