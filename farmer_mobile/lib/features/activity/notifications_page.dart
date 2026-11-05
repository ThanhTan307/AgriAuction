import 'dart:async';
import 'package:flutter/material.dart';
import '../../core/network/api_client.dart';
import '../../core/network/live_updates.dart';
import '../../core/ui/formatters.dart';
import 'transaction_pages.dart';

class ParsedNotification {
  final String title;
  final String? lotName;
  final String? detail;
  final IconData icon;
  final Color iconColor;
  final Color iconBg;

  const ParsedNotification({
    required this.title,
    this.lotName,
    this.detail,
    required this.icon,
    required this.iconColor,
    required this.iconBg,
  });
}

ParsedNotification parseNotificationItem(Map<String, dynamic> item) {
  final rawTitle = item['title']?.toString().trim() ?? '';
  final rawMsg = item['message']?.toString().trim() ?? '';
  final kind = item['kind']?.toString().trim() ?? '';

  String actionKey = '';
  String remaining = rawMsg;
  final verbMatch = RegExp(r'^([A-Za-z0-9_]+):\s*(.*)$').firstMatch(rawMsg);
  if (verbMatch != null) {
    actionKey = verbMatch.group(1)!;
    remaining = verbMatch.group(2)!.trim();
  } else if (kind.isNotEmpty) {
    actionKey = kind;
  }

  String friendlyTitle;
  IconData icon = Icons.notifications_none;
  Color iconColor = const Color(0xFF15803D);
  Color iconBg = const Color(0xFFDCFCE7);

  switch (actionKey) {
    case 'Deliver':
      friendlyTitle = 'Xác nhận đã giao hàng';
      icon = Icons.local_shipping_outlined;
      iconColor = const Color(0xFF2563EB);
      iconBg = const Color(0xFFDBEAFE);
      break;
    case 'Receive':
      friendlyTitle = 'Người mua đã nhận hàng';
      icon = Icons.inventory_outlined;
      iconColor = const Color(0xFF059669);
      iconBg = const Color(0xFFD1FAE5);
      break;
    case 'ConfirmPayment':
      friendlyTitle = 'Xác nhận thanh toán mô phỏng';
      icon = Icons.payments_outlined;
      iconColor = const Color(0xFF15803D);
      iconBg = const Color(0xFFDCFCE7);
      break;
    case 'RequestPayment':
      friendlyTitle = 'Yêu cầu thanh toán mô phỏng';
      icon = Icons.request_quote_outlined;
      iconColor = const Color(0xFFD97706);
      iconBg = const Color(0xFFFEF3C7);
      break;
    case 'Complete':
      friendlyTitle = 'Hồ sơ giao dịch đã hoàn tất';
      icon = Icons.task_alt_outlined;
      iconColor = const Color(0xFF166534);
      iconBg = const Color(0xFFDCFCE7);
      break;
    case 'WinnerRecorded':
      friendlyTitle = 'Đã xác định người thắng phiên';
      icon = Icons.emoji_events_outlined;
      iconColor = const Color(0xFFD97706);
      iconBg = const Color(0xFFFEF3C7);
      break;
    case 'LotSoldSimulation':
      friendlyTitle = 'Lô hàng đã đấu giá thành công';
      icon = Icons.gavel_outlined;
      iconColor = const Color(0xFF15803D);
      iconBg = const Color(0xFFDCFCE7);
      break;
    case 'AuctionClosedNoWinner':
      friendlyTitle = 'Phiên đấu giá kết thúc không có người thắng';
      icon = Icons.event_busy_outlined;
      iconColor = const Color(0xFF64748B);
      iconBg = const Color(0xFFF1F5F9);
      break;
    case 'DepositSettlementRecorded':
      friendlyTitle = 'Đã quyết toán cọc tham gia';
      icon = Icons.account_balance_wallet_outlined;
      iconColor = const Color(0xFF0284C7);
      iconBg = const Color(0xFFE0F2FE);
      break;
    case 'Refund':
    case 'SimulatedRefunded':
      friendlyTitle = 'Đã hoàn cọc mô phỏng';
      icon = Icons.replay_outlined;
      iconColor = const Color(0xFF0284C7);
      iconBg = const Color(0xFFE0F2FE);
      break;
    case 'Forfeit':
    case 'SimulatedForfeited':
      friendlyTitle = 'Đã tịch thu cọc mô phỏng';
      icon = Icons.gavel_outlined;
      iconColor = const Color(0xFFDC2626);
      iconBg = const Color(0xFFFEE2E2);
      break;
    case 'ComplaintCreated':
      friendlyTitle = 'Đã tiếp nhận khiếu nại mới';
      icon = Icons.report_problem_outlined;
      iconColor = const Color(0xFFDC2626);
      iconBg = const Color(0xFFFEE2E2);
      break;
    case 'ComplaintResponded':
      friendlyTitle = 'Đã có phản hồi/giải trình khiếu nại';
      icon = Icons.chat_bubble_outline;
      iconColor = const Color(0xFF2563EB);
      iconBg = const Color(0xFFDBEAFE);
      break;
    case 'ComplaintDecisionRecorded':
      friendlyTitle = 'Đã có quyết định xử lý khiếu nại';
      icon = Icons.verified_outlined;
      iconColor = const Color(0xFF15803D);
      iconBg = const Color(0xFFDCFCE7);
      break;
    default:
      if (rawTitle.isNotEmpty) {
        friendlyTitle = notificationMessage(rawTitle);
      } else {
        friendlyTitle = 'Thông báo hệ thống';
      }
      break;
  }

  remaining = remaining.replaceAll('Chỉ mô phỏng, không phát sinh tiền thật.', '').trim();
  if (remaining.endsWith('.')) {
    remaining = remaining.substring(0, remaining.length - 1).trim();
  }

  String? lotName;
  String? detail;

  if (actionKey == 'Refund' || actionKey == 'SimulatedRefunded') {
    final refund = RegExp(r'([\d.,]+)\s*VND', caseSensitive: false).firstMatch(rawMsg);
    if (refund != null) {
      detail = 'Số tiền hoàn: ${formatVnd(refund.group(1), 'VND')}';
    }
  }

  if (remaining.isNotEmpty) {
    lotName = remaining;
  }

  return ParsedNotification(
    title: friendlyTitle,
    lotName: lotName,
    detail: detail,
    icon: icon,
    iconColor: iconColor,
    iconBg: iconBg,
  );
}

class NotificationsPage extends StatefulWidget {
  const NotificationsPage({super.key, required this.api});
  final ApiClient api;
  @override
  State<NotificationsPage> createState() => _NotificationsState();
}

class _NotificationsState extends State<NotificationsPage> {
  late Future<dynamic> _future = _load();
  late final LiveUpdates _live;
  Timer? _poll;
  int _page = 1;

  Future<dynamic> _load() =>
      widget.api.get('/api/v1/me/notifications?page=$_page&pageSize=20');

  @override
  void initState() {
    super.initState();
    _live = LiveUpdates(widget.api, '/hubs/notifications', 'InboxChanged', () {
      if (mounted) {
        setState(() {
          _future = _load();
        });
      }
    });
    _live.connect();
    _poll = Timer.periodic(const Duration(seconds: 10), (_) {
      if (mounted) {
        setState(() {
          _future = _load();
        });
      }
    });
  }

  @override
  void dispose() {
    _poll?.cancel();
    _live.dispose();
    super.dispose();
  }

  Future<void> _markAsRead(String id) async {
    try {
      await widget.api.post('/api/v1/me/notifications/$id/read', {});
      if (mounted) {
        setState(() {
          _future = _load();
        });
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Không thể đánh dấu đã đọc: $e')),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Thông báo'),
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
                ? const Text('Không thể tải thông báo. Vui lòng thử lại.')
                : const CircularProgressIndicator(),
          );
        }
        final rows = (s.data['items'] as List?) ?? [];
        final unreadCount = int.tryParse(s.data['unreadCount']?.toString() ?? '0') ?? 0;
        final totalCount = int.tryParse(s.data['totalCount']?.toString() ?? '0') ?? 0;

        return ListView(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
          children: [
            // Header summary bar
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  const Text(
                    'Tất cả thông báo',
                    style: TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.bold,
                      color: Color(0xFF0F172A),
                    ),
                  ),
                  if (unreadCount > 0)
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                      decoration: BoxDecoration(
                        color: const Color(0xFFDCFCE7),
                        borderRadius: BorderRadius.circular(12),
                        border: Border.all(color: const Color(0xFFBBF7D0)),
                      ),
                      child: Text(
                        '$unreadCount chưa đọc',
                        style: const TextStyle(
                          fontSize: 12,
                          fontWeight: FontWeight.w600,
                          color: Color(0xFF166534),
                        ),
                      ),
                    ),
                ],
              ),
            ),

            if (rows.isEmpty)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 48),
                child: Center(
                  child: Column(
                    children: const [
                      Icon(Icons.notifications_none_outlined, size: 56, color: Color(0xFF94A3B8)),
                      SizedBox(height: 12),
                      Text(
                        'Chưa có thông báo nào.',
                        style: TextStyle(color: Color(0xFF64748B), fontSize: 14),
                      ),
                    ],
                  ),
                ),
              ),

            for (final n in rows)
              Builder(
                builder: (context) {
                  final map = Map<String, dynamic>.from(n as Map);
                  final isUnread = map['readAt'] == null;
                  final parsed = parseNotificationItem(map);
                  final hasTransaction = map['transactionId'] != null;

                  return Container(
                    margin: const EdgeInsets.only(bottom: 8),
                    decoration: BoxDecoration(
                      color: isUnread ? const Color(0xFFFAFDFA) : Colors.white,
                      borderRadius: BorderRadius.circular(10),
                      border: Border.all(
                        color: isUnread ? const Color(0xFF86EFAC) : const Color(0xFFE2E8F0),
                        width: isUnread ? 1.2 : 1,
                      ),
                    ),
                    child: InkWell(
                      borderRadius: BorderRadius.circular(10),
                      onTap: hasTransaction
                          ? () => Navigator.push(
                              context,
                              MaterialPageRoute<void>(
                                builder: (_) => TransactionDetailPage(
                                  api: widget.api,
                                  id: map['transactionId'].toString(),
                                ),
                              ),
                            )
                          : null,
                      child: Padding(
                        padding: const EdgeInsets.all(12),
                        child: Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            // Icon badge
                            Container(
                              width: 38,
                              height: 38,
                              decoration: BoxDecoration(
                                color: parsed.iconBg,
                                borderRadius: BorderRadius.circular(8),
                              ),
                              child: Icon(parsed.icon, size: 20, color: parsed.iconColor),
                            ),
                            const SizedBox(width: 12),
                            // Content
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Row(
                                    children: [
                                      if (isUnread) ...[
                                        Container(
                                          width: 7,
                                          height: 7,
                                          margin: const EdgeInsets.only(right: 6),
                                          decoration: const BoxDecoration(
                                            color: Color(0xFF15803D),
                                            shape: BoxShape.circle,
                                          ),
                                        ),
                                      ],
                                      Expanded(
                                        child: Text(
                                          parsed.title,
                                          style: TextStyle(
                                            fontSize: 14,
                                            fontWeight: isUnread ? FontWeight.bold : FontWeight.w600,
                                            color: const Color(0xFF0F172A),
                                          ),
                                        ),
                                      ),
                                    ],
                                  ),
                                  if (parsed.lotName != null) ...[
                                    const SizedBox(height: 3),
                                    Text(
                                      parsed.lotName!,
                                      style: const TextStyle(
                                        fontSize: 13,
                                        color: Color(0xFF334155),
                                      ),
                                    ),
                                  ],
                                  if (parsed.detail != null) ...[
                                    const SizedBox(height: 2),
                                    Text(
                                      parsed.detail!,
                                      style: const TextStyle(
                                        fontSize: 12,
                                        color: Color(0xFF15803D),
                                        fontWeight: FontWeight.w500,
                                      ),
                                    ),
                                  ],
                                  const SizedBox(height: 6),
                                  Row(
                                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                                    children: [
                                      Text(
                                        formatDateTime(map['createdAt']),
                                        style: const TextStyle(
                                          fontSize: 11,
                                          color: Color(0xFF94A3B8),
                                        ),
                                      ),
                                      if (hasTransaction)
                                        const Text(
                                          'Xem hồ sơ ›',
                                          style: TextStyle(
                                            fontSize: 12,
                                            fontWeight: FontWeight.w600,
                                            color: Color(0xFF15803D),
                                          ),
                                        ),
                                    ],
                                  ),
                                ],
                              ),
                            ),
                            // Action mark as read
                            if (isUnread)
                              Padding(
                                padding: const EdgeInsets.only(left: 4),
                                child: IconButton(
                                  icon: const Icon(
                                    Icons.done_all,
                                    size: 18,
                                    color: Color(0xFF15803D),
                                  ),
                                  tooltip: 'Đánh dấu đã đọc',
                                  padding: EdgeInsets.zero,
                                  constraints: const BoxConstraints(),
                                  onPressed: () => _markAsRead(map['id'].toString()),
                                ),
                              ),
                          ],
                        ),
                      ),
                    ),
                  );
                },
              ),

            // Pagination
            if (totalCount > 20)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 12),
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
                      icon: const Icon(Icons.chevron_left, size: 18),
                      label: const Text('Trước'),
                    ),
                    Text(
                      'Trang $_page / ${((totalCount - 1) ~/ 20) + 1}',
                      style: const TextStyle(fontSize: 13, color: Color(0xFF64748B)),
                    ),
                    TextButton.icon(
                      onPressed: _page * 20 < totalCount
                          ? () => setState(() {
                              _page++;
                              _future = _load();
                            })
                          : null,
                      icon: const Icon(Icons.chevron_right, size: 18),
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
