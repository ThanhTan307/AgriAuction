import 'dart:async';
import 'package:flutter/material.dart';
import '../../core/network/api_client.dart';
import '../../core/network/live_updates.dart';
import '../../core/ui/formatters.dart';
import 'transaction_pages.dart';

class FarmerAuctionsPage extends StatefulWidget {
  const FarmerAuctionsPage({super.key, required this.api});
  final ApiClient api;
  @override
  State<FarmerAuctionsPage> createState() => _AuctionsState();
}

class _AuctionsState extends State<FarmerAuctionsPage> {
  int _page = 1;
  late Future<dynamic> _future = _load();
  Future<dynamic> _load() =>
      widget.api.get('/api/v1/me/auctions?page=$_page&pageSize=20');

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Phiên của lô hàng'),
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
                  const Icon(Icons.error_outline, size: 48, color: Color(0xFFD97706)),
                  const SizedBox(height: 12),
                  Text('Không thể tải danh sách phiên: ${s.error}'),
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
        final rows = (s.data['items'] as List?) ?? [];
        final total = int.tryParse(s.data['total']?.toString() ?? '0') ?? 0;

        return ListView(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
          children: [
            if (rows.isEmpty)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 48),
                child: Center(
                  child: Column(
                    children: const [
                      Icon(Icons.gavel_outlined, size: 56, color: Color(0xFF94A3B8)),
                      SizedBox(height: 12),
                      Text(
                        'Chưa có phiên đấu giá nào được thiết lập cho lô hàng của bạn.',
                        textAlign: TextAlign.center,
                        style: TextStyle(color: Color(0xFF64748B), height: 1.5),
                      ),
                    ],
                  ),
                ),
              ),

            for (final a in rows)
              Container(
                margin: const EdgeInsets.only(bottom: 8),
                decoration: BoxDecoration(
                  color: Colors.white,
                  borderRadius: BorderRadius.circular(10),
                  border: Border.all(color: const Color(0xFFE2E8F0)),
                ),
                child: InkWell(
                  borderRadius: BorderRadius.circular(10),
                  onTap: () => Navigator.push(
                    context,
                    MaterialPageRoute<void>(
                      builder: (_) => FarmerRoomPage(
                        api: widget.api,
                        auctionId: a['id'].toString(),
                      ),
                    ),
                  ),
                  child: Padding(
                    padding: const EdgeInsets.all(14),
                    child: Row(
                      children: [
                        Container(
                          width: 40,
                          height: 40,
                          decoration: BoxDecoration(
                            color: const Color(0xFFFEF3C7),
                            borderRadius: BorderRadius.circular(8),
                          ),
                          child: const Icon(Icons.gavel, size: 20, color: Color(0xFFD97706)),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                a['content']?['title']?.toString() ?? 'Phiên đấu giá',
                                style: const TextStyle(
                                  fontWeight: FontWeight.bold,
                                  fontSize: 14,
                                  color: Color(0xFF0F172A),
                                ),
                              ),
                              const SizedBox(height: 6),
                              Row(
                                children: [
                                  _auctionStateBadge(a['state']?.toString() ?? ''),
                                  const SizedBox(width: 8),
                                  Expanded(
                                    child: Text(
                                      'Bắt đầu: ${formatDateTime(a['terms']?['startsAt'])}',
                                      style: const TextStyle(
                                        fontSize: 11,
                                        color: Color(0xFF64748B),
                                      ),
                                      overflow: TextOverflow.ellipsis,
                                    ),
                                  ),
                                ],
                              ),
                            ],
                          ),
                        ),
                        const Icon(Icons.chevron_right, size: 20, color: Color(0xFF94A3B8)),
                      ],
                    ),
                  ),
                ),
              ),

            if (total > 20)
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
                    Text('Trang $_page'),
                    TextButton.icon(
                      onPressed: _page * 20 < total
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

  Widget _auctionStateBadge(String state) {
    Color bg = const Color(0xFFF1F5F9);
    Color fg = const Color(0xFF475569);
    String label = transactionText(state);

    if (state == 'Closed' || state == 'Settled') {
      bg = const Color(0xFFF1F5F9);
      fg = const Color(0xFF475569);
      label = 'Đã chốt phiên';
    } else if (state == 'Active' || state == 'Running') {
      bg = const Color(0xFFDCFCE7);
      fg = const Color(0xFF166534);
      label = 'Đang diễn ra';
    } else if (state == 'Published' || state == 'Scheduled') {
      bg = const Color(0xFFDBEAFE);
      fg = const Color(0xFF1E40AF);
      label = 'Sắp mở';
    }

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: bg,
        borderRadius: BorderRadius.circular(6),
      ),
      child: Text(
        label,
        style: TextStyle(color: fg, fontSize: 11, fontWeight: FontWeight.w600),
      ),
    );
  }
}

class FarmerRoomPage extends StatefulWidget {
  const FarmerRoomPage({super.key, required this.api, required this.auctionId});
  final ApiClient api;
  final String auctionId;
  @override
  State<FarmerRoomPage> createState() => _FarmerRoomState();
}

class _FarmerRoomState extends State<FarmerRoomPage> {
  late Future<dynamic> _future = _load();
  late final LiveUpdates _live;
  Timer? _poll;
  final List<dynamic> _older = [];
  bool _loadingOlder = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _live = LiveUpdates(
      widget.api,
      '/hubs/simulation',
      'AuctionChanged',
      () {
        if (mounted) {
          setState(() {
            _future = _load();
          });
        }
      },
      joinMethod: 'JoinRoom',
      joinArguments: [widget.auctionId],
    );
    _live.connect();
    _poll = Timer.periodic(const Duration(seconds: 5), (_) {
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

  Future<dynamic> _load() =>
      widget.api.get('/api/v1/auctions/${widget.auctionId}/room');

  Future<void> _more(List<dynamic> bids) async {
    if (bids.isEmpty) return;
    setState(() {
      _loadingOlder = true;
      _error = null;
    });
    try {
      final before = bids
          .map((b) => BigInt.parse(b['version'].toString()))
          .reduce((a, b) => a < b ? a : b);
      final page = await widget.api.get(
        '/api/v1/auctions/${widget.auctionId}/room/bids?beforeVersion=$before&pageSize=100',
      );
      if (mounted) setState(() => _older.addAll(page['items'] as List));
    } catch (e) {
      if (mounted) setState(() => _error = '$e');
    } finally {
      if (mounted) setState(() => _loadingOlder = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Theo dõi phiên đấu giá'),
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
                  const Icon(Icons.error_outline, size: 48, color: Color(0xFFD97706)),
                  const SizedBox(height: 12),
                  Text(
                    'Phòng chưa được cấu hình hoặc không thể truy cập.\n${s.error}',
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 12),
                  FilledButton(
                    onPressed: () => setState(() => _future = _load()),
                    child: const Text('Thử lại'),
                  ),
                ],
              ),
            ),
          );
        }
        final r = s.data, result = r['result'];
        final unique = <String, dynamic>{};
        for (final b in [...(r['bids'] as List), ..._older]) {
          unique[b['id'].toString()] = b;
        }
        final bids = unique.values.toList()
          ..sort(
            (a, b) => BigInt.parse(
              b['version'].toString(),
            ).compareTo(BigInt.parse(a['version'].toString())),
          );

        final isClosed = r['state'] == 'Closed' || r['state'] == 'Settled';
        final currency = r['rules']?['currency']?.toString() ?? 'VND';

        return ListView(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
          children: [
            // Status and Lot Info Card
            Container(
              padding: const EdgeInsets.all(16),
              decoration: BoxDecoration(
                color: Colors.white,
                borderRadius: BorderRadius.circular(10),
                border: Border.all(color: const Color(0xFFE2E8F0)),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                        decoration: BoxDecoration(
                          color: isClosed ? const Color(0xFFF1F5F9) : const Color(0xFFDCFCE7),
                          borderRadius: BorderRadius.circular(6),
                        ),
                        child: Text(
                          isClosed ? 'Phiên đã chốt' : 'Phiên đang diễn ra',
                          style: TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.bold,
                            color: isClosed ? const Color(0xFF475569) : const Color(0xFF166534),
                          ),
                        ),
                      ),
                      Text(
                        'Hạn: ${formatDateTime(r['endsAt'])}',
                        style: const TextStyle(fontSize: 12, color: Color(0xFF64748B)),
                      ),
                    ],
                  ),
                  const SizedBox(height: 10),
                  Text(
                    r['title'].toString(),
                    style: const TextStyle(fontSize: 16, fontWeight: FontWeight.bold, color: Color(0xFF0F172A)),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    'Cơ sở tính giá: ${displayPriceBasis(r['rules']?['priceBasis'])}',
                    style: const TextStyle(fontSize: 12, color: Color(0xFF64748B)),
                  ),
                ],
              ),
            ),

            const SizedBox(height: 12),

            // Price / Result card
            if (result != null)
              Container(
                padding: const EdgeInsets.all(16),
                decoration: BoxDecoration(
                  color: const Color(0xFFF0FDF4),
                  borderRadius: BorderRadius.circular(10),
                  border: Border.all(color: const Color(0xFF86EFAC)),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Text(
                      'KẾT QUẢ CHỐT PHIÊN',
                      style: TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.bold,
                        color: Color(0xFF166534),
                        letterSpacing: 0.5,
                      ),
                    ),
                    const SizedBox(height: 6),
                    Text(
                      formatVnd(result['amount'], currency),
                      style: const TextStyle(
                        fontSize: 22,
                        fontWeight: FontWeight.bold,
                        color: Color(0xFF15803D),
                      ),
                    ),
                    const SizedBox(height: 4),
                    Text(
                      result['winner'] == null
                          ? 'Phiên kết thúc không có người thắng.'
                          : 'Bên mua trúng đấu giá: ${result['winner']}',
                      style: const TextStyle(fontSize: 13, color: Color(0xFF334155), fontWeight: FontWeight.w500),
                    ),
                  ],
                ),
              )
            else
              Container(
                padding: const EdgeInsets.all(16),
                decoration: BoxDecoration(
                  color: Colors.white,
                  borderRadius: BorderRadius.circular(10),
                  border: Border.all(color: const Color(0xFFE2E8F0)),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Text(
                      'GIÁ DẪN ĐẦU HIỆN TẠI',
                      style: TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.bold,
                        color: Color(0xFF64748B),
                        letterSpacing: 0.5,
                      ),
                    ),
                    const SizedBox(height: 6),
                    Text(
                      r['currentPrice'] != null
                          ? formatVnd(r['currentPrice'], currency)
                          : 'Chưa có lượt đặt giá',
                      style: const TextStyle(
                        fontSize: 22,
                        fontWeight: FontWeight.bold,
                        color: Color(0xFF15803D),
                      ),
                    ),
                    const SizedBox(height: 4),
                    const Text(
                      'Tự động cập nhật trực tiếp theo thời gian thực.',
                      style: TextStyle(fontSize: 11, color: Color(0xFF94A3B8)),
                    ),
                  ],
                ),
              ),

            const SizedBox(height: 16),

            // Bid history header
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                const Text(
                  'Lịch sử trả giá',
                  style: TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.bold,
                    color: Color(0xFF0F172A),
                  ),
                ),
                Text(
                  '${bids.length} lượt',
                  style: const TextStyle(fontSize: 12, color: Color(0xFF64748B)),
                ),
              ],
            ),
            const SizedBox(height: 8),

            if (bids.isEmpty)
              Container(
                padding: const EdgeInsets.symmetric(vertical: 24),
                decoration: BoxDecoration(
                  color: Colors.white,
                  borderRadius: BorderRadius.circular(10),
                  border: Border.all(color: const Color(0xFFE2E8F0)),
                ),
                child: const Center(
                  child: Text(
                    'Chưa có lượt đặt giá nào.',
                    style: TextStyle(fontSize: 13, color: Color(0xFF94A3B8)),
                  ),
                ),
              ),

            for (final b in bids)
              Container(
                margin: const EdgeInsets.only(bottom: 6),
                padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                decoration: BoxDecoration(
                  color: Colors.white,
                  borderRadius: BorderRadius.circular(8),
                  border: Border.all(color: const Color(0xFFE2E8F0)),
                ),
                child: Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          formatVnd(b['amount'], currency),
                          style: const TextStyle(
                            fontSize: 14,
                            fontWeight: FontWeight.bold,
                            color: Color(0xFF15803D),
                          ),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          b['bidder']?.toString() ?? 'Bên mua',
                          style: const TextStyle(fontSize: 12, color: Color(0xFF475569)),
                        ),
                      ],
                    ),
                    Text(
                      formatDateTime(b['acceptedAt']),
                      style: const TextStyle(fontSize: 11, color: Color(0xFF94A3B8)),
                    ),
                  ],
                ),
              ),

            if (_error != null)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(
                  _error!,
                  style: const TextStyle(color: Color(0xFFDC2626), fontSize: 12),
                ),
              ),

            if (bids.length < int.parse(r['bidCount']?.toString() ?? '0'))
              Padding(
                padding: const EdgeInsets.only(top: 10),
                child: OutlinedButton(
                  onPressed: _loadingOlder ? null : () => _more(bids),
                  child: _loadingOlder
                      ? const SizedBox(
                          width: 16,
                          height: 16,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Text('Tải thêm giá đặt cũ hơn'),
                ),
              ),
          ],
        );
      },
    ),
  );
}
