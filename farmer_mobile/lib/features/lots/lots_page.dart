import 'package:flutter/material.dart';

import '../../core/network/api_client.dart';
import '../../core/network/live_updates.dart';
import '../../core/ui/formatters.dart';
import 'lot_detail_page.dart';
import 'lot_editor_page.dart';
import 'lot_models.dart';

class LotsPage extends StatefulWidget {
  const LotsPage({super.key, required this.api});
  final ApiClient api;
  @override
  State<LotsPage> createState() => _LotsPageState();
}

class _LotsPageState extends State<LotsPage> {
  late Future<List<LotItem>> _future = _load();
  LiveUpdates? _live;

  @override
  void initState() {
    super.initState();
    _live = LiveUpdates(
      widget.api,
      '/hubs/notifications',
      'InboxChanged',
      () {
        if (mounted) _refresh();
      },
    );
    _live?.connect();
  }

  @override
  void dispose() {
    _live?.dispose();
    super.dispose();
  }

  Future<List<LotItem>> _load() async {
    final json = Map<String, dynamic>.from(
      await widget.api.get('/api/v1/lots?page=1&pageSize=100'),
    );
    return ((json['items'] as List?) ?? const [])
        .map((e) => LotItem.fromJson(Map<String, dynamic>.from(e as Map)))
        .toList();
  }

  Future<void> _refresh() async => setState(() {
    _future = _load();
  });

  @override
  Widget build(BuildContext context) => FutureBuilder<List<LotItem>>(
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
                const Text(
                  'Không tải được danh sách lô hàng.',
                  style: TextStyle(fontWeight: FontWeight.w600),
                ),
                const SizedBox(height: 12),
                FilledButton.icon(
                  onPressed: _refresh,
                  icon: const Icon(Icons.refresh),
                  label: const Text('Thử lại'),
                ),
              ],
            ),
          ),
        );
      }
      final items = snapshot.data ?? [];
      return RefreshIndicator(
        onRefresh: _refresh,
        child: ListView(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
          children: [
            // Top action bar
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text(
                    '${items.length} lô nông sản',
                    style: const TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.bold,
                      color: Color(0xFF0F172A),
                    ),
                  ),
                  FilledButton.icon(
                    onPressed: () async {
                      await Navigator.push(
                        context,
                        MaterialPageRoute(
                          builder: (_) => LotEditorPage(api: widget.api),
                        ),
                      );
                      _refresh();
                    },
                    icon: const Icon(Icons.add, size: 18),
                    label: const Text('Tạo lô mới'),
                    style: FilledButton.styleFrom(
                      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
                      minimumSize: const Size(0, 38),
                      backgroundColor: const Color(0xFF15803D),
                    ),
                  ),
                ],
              ),
            ),

            if (items.isEmpty)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 48),
                child: Center(
                  child: Column(
                    children: const [
                      Icon(Icons.inventory_2_outlined, size: 56, color: Color(0xFF94A3B8)),
                      SizedBox(height: 12),
                      Text(
                        'Bạn chưa có lô hàng nào.\nBấm "Tạo lô mới" để bắt đầu đăng ký nông sản.',
                        textAlign: TextAlign.center,
                        style: TextStyle(color: Color(0xFF64748B), height: 1.5),
                      ),
                    ],
                  ),
                ),
              ),

            ...items.map(
              (lot) => Container(
                margin: const EdgeInsets.only(bottom: 8),
                decoration: BoxDecoration(
                  color: Colors.white,
                  borderRadius: BorderRadius.circular(10),
                  border: Border.all(color: const Color(0xFFE2E8F0)),
                ),
                child: InkWell(
                  borderRadius: BorderRadius.circular(10),
                  onTap: () async {
                    await Navigator.push(
                      context,
                      MaterialPageRoute(
                        builder: (_) => LotDetailPage(api: widget.api, lotId: lot.id),
                      ),
                    );
                    _refresh();
                  },
                  child: Padding(
                    padding: const EdgeInsets.all(12),
                    child: Row(
                      children: [
                        // Produce leading icon
                        Container(
                          width: 40,
                          height: 40,
                          decoration: BoxDecoration(
                            color: const Color(0xFFDCFCE7),
                            borderRadius: BorderRadius.circular(8),
                          ),
                          child: const Icon(
                            Icons.eco_outlined,
                            size: 22,
                            color: Color(0xFF15803D),
                          ),
                        ),
                        const SizedBox(width: 12),
                        // Lot details
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                lot.title,
                                style: const TextStyle(
                                  fontWeight: FontWeight.bold,
                                  fontSize: 14,
                                  color: Color(0xFF0F172A),
                                ),
                              ),
                              const SizedBox(height: 6),
                              Row(
                                children: [
                                  _stateBadge(lot.state),
                                  if (lot.submittedAt != null) ...[
                                    const SizedBox(width: 8),
                                    Text(
                                      formatDateTime(lot.submittedAt),
                                      style: const TextStyle(
                                        fontSize: 11,
                                        color: Color(0xFF94A3B8),
                                      ),
                                    ),
                                  ],
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
            ),
          ],
        ),
      );
    },
  );

  Widget _stateBadge(String state) {
    Color bg = const Color(0xFFF1F5F9);
    Color fg = const Color(0xFF475569);
    String label = state;

    switch (state) {
      case 'Draft':
        label = 'Bản nháp';
        bg = const Color(0xFFF1F5F9);
        fg = const Color(0xFF475569);
        break;
      case 'PendingReview':
        label = 'Đang chờ duyệt';
        bg = const Color(0xFFFEF3C7);
        fg = const Color(0xFF92400E);
        break;
      case 'Approved':
        label = 'Đã phê duyệt';
        bg = const Color(0xFFDCFCE7);
        fg = const Color(0xFF166534);
        break;
      case 'Rejected':
        label = 'Bị từ chối';
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
        style: TextStyle(color: fg, fontSize: 11, fontWeight: FontWeight.w600),
      ),
    );
  }
}
