import 'package:flutter/material.dart';

import '../../core/network/api_client.dart';
import '../../core/network/live_updates.dart';
import '../../core/ui/formatters.dart';
import 'lot_editor_page.dart';
import 'lot_models.dart';

class LotDetailPage extends StatefulWidget {
  const LotDetailPage({super.key, required this.api, required this.lotId});
  final ApiClient api;
  final String lotId;
  @override
  State<LotDetailPage> createState() => _LotDetailPageState();
}

class _LotDetailPageState extends State<LotDetailPage> {
  late Future<LotDetail> _future = _load();
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

  Future<LotDetail> _load() async => LotDetail.fromJson(
    Map<String, dynamic>.from(
      await widget.api.get('/api/v1/lots/${widget.lotId}'),
    ),
  );

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Chi tiết lô nông sản'),
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
    body: FutureBuilder<LotDetail>(
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
                  const Icon(Icons.error_outline, size: 48, color: Colors.amber),
                  const SizedBox(height: 12),
                  Text('Không tải được lô hàng: ${snapshot.error}'),
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
        final lot = snapshot.data!;
        final editable = lot.state == 'Draft' || lot.state == 'Rejected';
        final reviews = lot.content['qualityNotes']?.toString();
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Text(
              lot.title,
              style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                    fontWeight: FontWeight.bold,
                    color: const Color(0xFF0F172A),
                  ),
            ),
            const SizedBox(height: 8),
            Row(
              children: [
                _stateBadge(lot.state),
              ],
            ),
            if (lot.state == 'Rejected' && lot.content.isNotEmpty) ...[
              const SizedBox(height: 12),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: const Color(0xFFFEF2F2),
                  borderRadius: BorderRadius.circular(8),
                  border: Border.all(color: const Color(0xFFFECACA)),
                ),
                child: const Row(
                  children: [
                    Icon(Icons.info_outline, size: 20, color: Color(0xFFB91C1C)),
                    SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        'Hồ sơ lô bị từ chối duyệt. Bạn có thể chỉnh sửa thông tin và gửi lại.',
                        style: TextStyle(fontSize: 13, color: Color(0xFFB91C1C)),
                      ),
                    ),
                  ],
                ),
              ),
            ],
            const SizedBox(height: 16),
            Card(
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(10),
                side: const BorderSide(color: Color(0xFFE2E8F0)),
              ),
              child: Padding(
                padding: const EdgeInsets.all(14),
                child: Column(
                  children: [
                    _infoRow('Danh mục nông sản', lot.categoryName),
                    _infoRow(
                      'Số lượng khai báo',
                      '${lot.content['quantity']} ${lot.content['quantityUnit']}',
                    ),
                    _infoRow(
                      'Khối lượng khai báo',
                      '${lot.content['weight']} ${lot.content['weightUnit']}',
                    ),
                    _infoRow(
                      'Giá khởi điểm đề xuất',
                      formatVnd(lot.content['proposedStartingPrice'], lot.content['currency']?.toString()),
                    ),
                    _infoRow('Địa điểm khai báo', lot.content['location']?.toString() ?? 'Chưa cung cấp'),
                    if (reviews != null && reviews.isNotEmpty)
                      _infoRow('Thông tin chất lượng', reviews),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 16),
            if (lot.images.isNotEmpty) ...[
              const Text(
                'Hình ảnh nông sản',
                style: TextStyle(fontWeight: FontWeight.bold, fontSize: 14),
              ),
              const SizedBox(height: 8),
              SizedBox(
                height: 130,
                child: ListView(
                  scrollDirection: Axis.horizontal,
                  children: lot.images
                      .map(
                        (image) => Padding(
                          padding: const EdgeInsets.only(right: 10),
                          child: ClipRRect(
                            borderRadius: BorderRadius.circular(8),
                            child: Image.network(
                              '${widget.api.config.baseUrl}${image.url}',
                              headers: widget.api.authHeaders,
                              width: 140,
                              height: 130,
                              fit: BoxFit.cover,
                              errorBuilder: (context, error, stackTrace) => Container(
                                width: 140,
                                height: 130,
                                color: const Color(0xFFF1F5F9),
                                child: const Icon(Icons.image_not_supported, color: Color(0xFF94A3B8)),
                              ),
                            ),
                          ),
                        ),
                      )
                      .toList(),
                ),
              ),
              const SizedBox(height: 16),
            ],
            if (editable)
              FilledButton.icon(
                onPressed: () async {
                  await Navigator.push(
                    context,
                    MaterialPageRoute(
                      builder: (_) =>
                          LotEditorPage(api: widget.api, existing: lot),
                    ),
                  );
                  if (mounted) {
                    setState(() {
                      _future = _load();
                    });
                  }
                },
                icon: const Icon(Icons.edit),
                label: const Text('Chỉnh sửa và gửi lại'),
                style: FilledButton.styleFrom(
                  minimumSize: const Size.fromHeight(48),
                  backgroundColor: const Color(0xFF15803D),
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
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: bg,
        borderRadius: BorderRadius.circular(6),
      ),
      child: Text(
        label,
        style: TextStyle(color: fg, fontSize: 12, fontWeight: FontWeight.w600),
      ),
    );
  }
}
