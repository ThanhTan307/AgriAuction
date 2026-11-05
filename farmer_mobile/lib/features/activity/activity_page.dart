import 'package:flutter/material.dart';
import '../../core/network/api_client.dart';
import 'auctions_page.dart';
import 'transaction_pages.dart';
import 'notifications_page.dart';
import 'complaint_pages.dart';
import 'report_page.dart';

class ActivityPage extends StatelessWidget {
  const ActivityPage({super.key, required this.api});
  final ApiClient api;

  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
    children: [
      _link(
        context,
        'Phiên của lô hàng',
        'Theo dõi trực tiếp diễn biến và kết quả các phiên đấu giá',
        Icons.gavel,
        const Color(0xFFD97706),
        const Color(0xFFFEF3C7),
        FarmerAuctionsPage(api: api),
      ),
      _link(
        context,
        'Giao dịch & giao nhận',
        'Quản lý tiến trình thanh toán, giao hàng và hoàn tất hồ sơ',
        Icons.receipt_long_outlined,
        const Color(0xFF0284C7),
        const Color(0xFFE0F2FE),
        TransactionsPage(api: api),
      ),
      _link(
        context,
        'Thông báo',
        'Cập nhật thông báo hệ thống, biến động cọc và kết quả phiên',
        Icons.notifications_outlined,
        const Color(0xFF15803D),
        const Color(0xFFDCFCE7),
        NotificationsPage(api: api),
      ),
      _link(
        context,
        'Khiếu nại & giải trình',
        'Tiếp nhận, phản hồi ý kiến và giải quyết tranh chấp giao dịch',
        Icons.forum_outlined,
        const Color(0xFF7C3AED),
        const Color(0xFFEDE9FE),
        ComplaintsPage(api: api),
      ),
      _link(
        context,
        'Báo cáo cá nhân',
        'Thống kê doanh thu mô phỏng và tỷ lệ hoàn tất giao dịch',
        Icons.bar_chart_outlined,
        const Color(0xFF059669),
        const Color(0xFFD1FAE5),
        FarmerReportPage(api: api),
      ),
    ],
  );

  Widget _link(
    BuildContext context,
    String title,
    String subtitle,
    IconData icon,
    Color iconColor,
    Color iconBg,
    Widget page,
  ) => Container(
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
        MaterialPageRoute<void>(builder: (_) => page),
      ),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
        child: Row(
          children: [
            Container(
              width: 40,
              height: 40,
              decoration: BoxDecoration(
                color: iconBg,
                borderRadius: BorderRadius.circular(8),
              ),
              child: Icon(icon, size: 20, color: iconColor),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    title,
                    style: const TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.bold,
                      color: Color(0xFF0F172A),
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    subtitle,
                    style: const TextStyle(
                      fontSize: 12,
                      color: Color(0xFF64748B),
                    ),
                  ),
                ],
              ),
            ),
            const Icon(Icons.chevron_right, size: 20, color: Color(0xFF94A3B8)),
          ],
        ),
      ),
    ),
  );
}
