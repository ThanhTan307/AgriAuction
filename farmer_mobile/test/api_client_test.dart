import 'package:flutter_test/flutter_test.dart';
import 'dart:convert';
import 'dart:io';

import 'package:farmer_mobile/core/config/app_config.dart';
import 'package:farmer_mobile/core/network/api_client.dart';
import 'package:farmer_mobile/features/lots/lot_editor_page.dart';

void main() {
  test('config trims trailing slash and keeps emulator URL configurable', () {
    const config = AppConfig(baseUrl: 'http://10.0.2.2:5080');
    final client = ApiClient(config);
    expect(client.config.baseUrl, 'http://10.0.2.2:5080');
  });

  test('API exception exposes status and safe user message', () {
    const error = ApiException(403, 'Không có quyền.');
    expect(error.statusCode, 403);
    expect(error.toString(), 'Không có quyền.');
  });

  test('ASP.NET field errors become Vietnamese field messages', () async {
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    try {
      server.listen((request) async {
        request.response
          ..statusCode = 400
          ..headers.contentType = ContentType.json
          ..write(
            jsonEncode({
              'title': 'One or more validation errors occurred.',
              'errors': {
                r'$.content.proposedStartingPrice': [
                  'The JSON value could not be converted to System.Decimal.',
                ],
                'request': ['The request field is required.'],
              },
            }),
          );
        await request.response.close();
      });
      final api = ApiClient(
        AppConfig(baseUrl: 'http://127.0.0.1:${server.port}'),
      );
      await expectLater(
        api.post('/api/v1/lots', const {}),
        throwsA(
          isA<ApiException>()
              .having(
                (e) => e.message,
                'message',
                'Giá trị phải là số hợp lệ. Có thể nhập 2000000 hoặc 2.000.000.',
              )
              .having(
                (e) => e.fieldErrors['proposedStartingPrice'],
                'field error',
                'Giá trị phải là số hợp lệ. Có thể nhập 2000000 hoặc 2.000.000.',
              ),
        ),
      );
    } finally {
      await server.close(force: true);
    }
  });

  test('Vietnamese grouped decimals are normalized for API payloads', () {
    expect(normalizeLotDecimal('2.000.000'), '2000000');
    expect(normalizeLotDecimal('100.000'), '100000');
    expect(normalizeLotDecimal('2,5'), '2.5');
    expect(normalizeLotDecimal('1.234,56'), '1234.56');
    expect(normalizeLotDecimal('1234.5678'), '1234.5678');
  });
}
