import 'package:flutter/material.dart';
import '../../core/network/api_client.dart';
import '../../core/ui/formatters.dart';

class FarmerReportPage extends StatefulWidget {
  const FarmerReportPage({super.key, required this.api});
  final ApiClient api;
  @override
  State<FarmerReportPage> createState() => _FarmerReportState();
}

class _FarmerReportState extends State<FarmerReportPage> {
  DateTime _from = DateTime(DateTime.now().year, DateTime.now().month, 1),
      _through = DateTime.now();
  Future<dynamic>? _future;

  String _apiDate(DateTime d) =>
      '${d.year.toString().padLeft(4, '0')}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';

  String _displayDate(DateTime d) =>
      '${d.day.toString().padLeft(2, '0')}/${d.month.toString().padLeft(2, '0')}/${d.year}';

  Future<dynamic> _load() => widget.api.get(
    '/api/v1/me/reports/completion?from=${_apiDate(_from)}&through=${_apiDate(_through)}',
  );

  @override
  void initState() {
    super.initState();
    _future = _load();
  }

  Future<void> _pick(bool from) async {
    final d = await showDatePicker(
      context: context,
      initialDate: from ? _from : _through,
      firstDate: DateTime(2020),
      lastDate: DateTime(2099, 12, 31),
    );
    if (d != null && mounted) {
      setState(() {
        if (from) {
          _from = d;
          if (_from.isAfter(_through)) _through = _from;
        } else {
          _through = d;
          if (_through.isBefore(_from)) _from = _through;
        }
        _future = _load();
      });
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Báo cáo cá nhân'),
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
    body: ListView(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      children: [
        // Date range selector card
        Container(
          padding: const EdgeInsets.all(14),
          decoration: BoxDecoration(
            color: Colors.white,
            borderRadius: BorderRadius.circular(10),
            border: Border.all(color: const Color(0xFFE2E8F0)),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Row(
                children: [
                  Icon(Icons.calendar_today_outlined, size: 16, color: Color(0xFF15803D)),
                  SizedBox(width: 6),
                  Text(
                    'Kỳ báo cáo (dd/MM/yyyy)',
                    style: TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.bold,
                      color: Color(0xFF0F172A),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 10),
              Row(
                children: [
                  Expanded(
                    child: OutlinedButton(
                      style: OutlinedButton.styleFrom(
                        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 10),
                        side: const BorderSide(color: Color(0xFFCBD5E1)),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                      ),
                      onPressed: () => _pick(true),
                      child: Row(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          const Text('Từ: ', style: TextStyle(fontSize: 12, color: Color(0xFF64748B))),
                          Text(
                            _displayDate(_from),
                            style: const TextStyle(fontSize: 13, fontWeight: FontWeight.bold, color: Color(0xFF0F172A)),
                          ),
                        ],
                      ),
                    ),
                  ),
                  const Padding(
                    padding: EdgeInsets.symmetric(horizontal: 8),
                    child: Icon(Icons.arrow_forward, size: 16, color: Color(0xFF94A3B8)),
                  ),
                  Expanded(
                    child: OutlinedButton(
                      style: OutlinedButton.styleFrom(
                        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 10),
                        side: const BorderSide(color: Color(0xFFCBD5E1)),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                      ),
                      onPressed: () => _pick(false),
                      child: Row(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          const Text('Đến: ', style: TextStyle(fontSize: 12, color: Color(0xFF64748B))),
                          Text(
                            _displayDate(_through),
                            style: const TextStyle(fontSize: 13, fontWeight: FontWeight.bold, color: Color(0xFF0F172A)),
                          ),
                        ],
                      ),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),

        const SizedBox(height: 14),

        FutureBuilder<dynamic>(
          future: _future,
          builder: (context, s) {
            if (s.connectionState == ConnectionState.waiting && !s.hasData) {
              return const Padding(
                padding: EdgeInsets.symmetric(vertical: 48),
                child: Center(child: CircularProgressIndicator()),
              );
            }
            if (s.hasError) {
              return Container(
                padding: const EdgeInsets.all(16),
                decoration: BoxDecoration(
                  color: const Color(0xFFFEF2F2),
                  borderRadius: BorderRadius.circular(10),
                  border: Border.all(color: const Color(0xFFFECACA)),
                ),
                child: Column(
                  children: [
                    const Icon(Icons.error_outline, size: 36, color: Color(0xFFDC2626)),
                    const SizedBox(height: 8),
                    Text(
                      'Không thể tải báo cáo: ${s.error}',
                      textAlign: TextAlign.center,
                      style: const TextStyle(color: Color(0xFFB91C1C), fontSize: 13),
                    ),
                    const SizedBox(height: 12),
                    FilledButton.icon(
                      onPressed: () => setState(() => _future = _load()),
                      icon: const Icon(Icons.refresh, size: 18),
                      label: const Text('Thử lại'),
                    ),
                  ],
                ),
              );
            }
            final r = s.data;
            if (r == null) {
              return const Padding(
                padding: EdgeInsets.symmetric(vertical: 40),
                child: Center(child: Text('Không có dữ liệu báo cáo.')),
              );
            }

            final formattedRevenue = formatVnd(r['simulatedRevenue'], r['currency']?.toString());
            final completedCount = r['completedInPeriod']?.toString() ?? '0';
            final successRate = r['successRatePercent'];
            final cohort = '${r['completedWinningCohort'] ?? 0}/${r['winningAuctions'] ?? 0}';

            return Column(
              children: [
                // Hero revenue card
                Container(
                  width: double.infinity,
                  padding: const EdgeInsets.all(18),
                  decoration: BoxDecoration(
                    color: Colors.white,
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(color: const Color(0xFF86EFAC), width: 1.2),
                    boxShadow: const [
                      BoxShadow(
                        color: Color(0x0815803D),
                        blurRadius: 8,
                        offset: Offset(0, 2),
                      ),
                    ],
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Row(
                        mainAxisAlignment: MainAxisAlignment.spaceBetween,
                        children: [
                          const Text(
                            'DOANH THU MÔ PHỎNG',
                            style: TextStyle(
                              fontSize: 12,
                              fontWeight: FontWeight.bold,
                              color: Color(0xFF166534),
                              letterSpacing: 0.5,
                            ),
                          ),
                          Container(
                            padding: const EdgeInsets.all(6),
                            decoration: const BoxDecoration(
                              color: Color(0xFFDCFCE7),
                              shape: BoxShape.circle,
                            ),
                            child: const Icon(Icons.trending_up, size: 18, color: Color(0xFF15803D)),
                          ),
                        ],
                      ),
                      const SizedBox(height: 8),
                      Text(
                        formattedRevenue,
                        style: const TextStyle(
                          fontSize: 26,
                          fontWeight: FontWeight.bold,
                          color: Color(0xFF15803D),
                        ),
                      ),
                      const SizedBox(height: 6),
                      const Text(
                        'Tổng giá trị các giao dịch chốt thành công trong kỳ (mô phỏng, không phát sinh tiền thật)',
                        style: TextStyle(fontSize: 12, color: Color(0xFF64748B)),
                      ),
                    ],
                  ),
                ),

                const SizedBox(height: 12),

                // Metrics row: Giao dịch & Tỷ lệ
                Row(
                  children: [
                    Expanded(
                      child: Container(
                        padding: const EdgeInsets.all(14),
                        decoration: BoxDecoration(
                          color: Colors.white,
                          borderRadius: BorderRadius.circular(10),
                          border: Border.all(color: const Color(0xFFE2E8F0)),
                        ),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            const Row(
                              children: [
                                Icon(Icons.receipt_long_outlined, size: 16, color: Color(0xFF0284C7)),
                                SizedBox(width: 4),
                                Expanded(
                                  child: Text(
                                    'Giao dịch hoàn tất',
                                    style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: Color(0xFF475569)),
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                ),
                              ],
                            ),
                            const SizedBox(height: 8),
                            Text(
                              completedCount,
                              style: const TextStyle(
                                fontSize: 22,
                                fontWeight: FontWeight.bold,
                                color: Color(0xFF0F172A),
                              ),
                            ),
                            const SizedBox(height: 4),
                            const Text(
                              'Giao dịch thành công',
                              style: TextStyle(fontSize: 11, color: Color(0xFF94A3B8)),
                            ),
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Container(
                        padding: const EdgeInsets.all(14),
                        decoration: BoxDecoration(
                          color: Colors.white,
                          borderRadius: BorderRadius.circular(10),
                          border: Border.all(color: const Color(0xFFE2E8F0)),
                        ),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            const Row(
                              children: [
                                Icon(Icons.verified_outlined, size: 16, color: Color(0xFF15803D)),
                                SizedBox(width: 4),
                                Expanded(
                                  child: Text(
                                    'Tỷ lệ thành công',
                                    style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: Color(0xFF475569)),
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                ),
                              ],
                            ),
                            const SizedBox(height: 8),
                            Text(
                              successRate == null ? 'Chưa có' : '$successRate%',
                              style: TextStyle(
                                fontSize: 22,
                                fontWeight: FontWeight.bold,
                                color: successRate == null ? const Color(0xFF64748B) : const Color(0xFF15803D),
                              ),
                            ),
                            const SizedBox(height: 4),
                            Text(
                              '$cohort phiên có người thắng',
                              style: const TextStyle(fontSize: 11, color: Color(0xFF94A3B8)),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ],
                ),

                const SizedBox(height: 16),

                // Footnote card
                Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: const Color(0xFFF1F5F9),
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: const Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Icon(Icons.info_outline, size: 16, color: Color(0xFF64748B)),
                      SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          'Số liệu theo giờ Việt Nam (UTC+7). Doanh thu mô phỏng được tính theo ngày hoàn tất giao dịch; tỷ lệ thành công tính theo nhóm phiên chốt có người thắng trong kỳ.',
                          style: TextStyle(fontSize: 11, color: Color(0xFF475569), height: 1.4),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            );
          },
        ),
      ],
    ),
  );
}
