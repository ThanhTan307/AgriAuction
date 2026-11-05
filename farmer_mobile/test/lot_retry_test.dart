import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:farmer_mobile/core/config/app_config.dart';
import 'package:farmer_mobile/core/network/api_client.dart';
import 'package:farmer_mobile/features/lots/lot_editor_page.dart';

class DraftApi extends ApiClient {
  DraftApi() : super(const AppConfig(baseUrl: 'http://127.0.0.1:1'));
  int creates = 0, updates = 0, submits = 0;
  Map<String, dynamic> content = {};
  Map<String, dynamic> detail(int version) => {
    'id': 'saved-draft',
    'state': 'Draft',
    'version': version,
    'content': content,
    'images': [],
  };
  @override
  Future<dynamic> get(String path) async => {
    'items': [
      {'id': 'fruit', 'name': 'Fruit', 'active': true},
    ],
  };
  @override
  Future<dynamic> post(
    String path, [
    Object? body,
    Map<String, String>? headers,
  ]) async {
    if (path == '/api/v1/lots') {
      creates++;
      content = Map<String, dynamic>.from((body as Map)['content']);
      return detail(1);
    }
    expect(path, '/api/v1/lots/saved-draft/submit');
    submits++;
    throw const ApiException(400, 'Ảnh chưa hợp lệ.');
  }

  @override
  Future<dynamic> patch(String path, Object body) async {
    expect(path, '/api/v1/lots/saved-draft');
    expect((body as Map)['expectedVersion'], 1);
    updates++;
    return detail(2);
  }
}

void main() {
  testWidgets('retry after known submit failure keeps the created draft', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(800, 2600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = DraftApi();
    await tester.pumpWidget(MaterialApp(home: LotEditorPage(api: api)));
    await tester.pumpAndSettle();
    await tester.tap(find.byType(DropdownButtonFormField<String>));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Fruit').last);
    await tester.pumpAndSettle();
    final fields = find.byType(TextFormField);
    final values = [
      'Synthetic lot',
      'Synthetic description',
      'Synthetic location',
      '100',
      'kg',
      '100',
      'kg',
      '2000000',
      '100000',
      'VND',
      'whole lot simulation',
      'Synthetic quality',
    ];
    for (var i = 0; i < values.length; i++) {
      await tester.enterText(fields.at(i), values[i]);
    }
    await tester.tap(find.text('Gửi kiểm duyệt'));
    await tester.pumpAndSettle();
    expect(find.text('Ảnh chưa hợp lệ.'), findsOneWidget);
    await tester.tap(find.text('Gửi kiểm duyệt'));
    await tester.pumpAndSettle();
    expect(api.creates, 1);
    expect(api.updates, 1);
    expect(api.submits, 2);
    expect(tester.takeException(), isNull);
  });
}
