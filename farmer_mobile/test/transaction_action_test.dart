import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:farmer_mobile/core/config/app_config.dart';
import 'package:farmer_mobile/core/network/api_client.dart';
import 'package:farmer_mobile/features/activity/transaction_pages.dart';

class TransactionApi extends ApiClient {
  TransactionApi() : super(const AppConfig(baseUrl: 'http://127.0.0.1:1'));
  bool delivered = false;
  int writes = 0;
  @override
  Future<dynamic> get(String path) async => {
    'transaction': {
      'id': 'synthetic',
      'title': 'Synthetic lot',
      'winningPrice': '123456.7890',
      'currency': 'VND',
      'priceBasis': 'whole lot simulation',
      'state': 'InProgress',
      'paymentState': 'SimulatedConfirmed',
      'deliveryState': delivered ? 'Delivered' : 'AwaitingDelivery',
      'version': delivered ? '4' : '3',
    },
    'allowedActions': delivered ? <String>[] : ['Deliver'],
    'history': [],
  };
  @override
  Future<dynamic> post(
    String path, [
    Object? body,
    Map<String, String>? headers,
  ]) async {
    writes++;
    delivered = true;
    return {'deliveryState': 'Delivered'};
  }
}

void main() {
  testWidgets(
    'confirmed delivery refreshes without a false unknown-result error',
    (tester) async {
      final api = TransactionApi();
      await tester.pumpWidget(
        MaterialApp(
          home: TransactionDetailPage(api: api, id: 'synthetic'),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Xác nhận đã giao'));
      await tester.pumpAndSettle();
      expect(api.writes, 1);
      expect(find.text('Farmer đã xác nhận giao'), findsOneWidget);
      expect(find.textContaining('Chưa xác nhận kết quả'), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );
}
