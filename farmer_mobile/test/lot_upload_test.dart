import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:farmer_mobile/core/config/app_config.dart';
import 'package:farmer_mobile/core/network/api_client.dart';

void main() {
  for (final extension in ['png', 'jpg', 'jpeg-with-png-name']) {
    test('lot $extension upload sends image MIME and version', () async {
      final directory = await Directory.systemTemp.createTemp('lot-upload-');
      final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      try {
        final png = extension == 'png';
        final file = File(
          '${directory.path}/fixture.${png || extension == 'jpeg-with-png-name' ? 'png' : 'jpg'}',
        );
        await file.writeAsBytes(
          png ? [137, 80, 78, 71, 13, 10, 26, 10] : [255, 216, 255, 224],
        );
        final api = ApiClient(
          AppConfig(baseUrl: 'http://127.0.0.1:${server.port}'),
        )..setToken('synthetic-token');
        final received = server.first.then((request) async {
          expect(request.uri.path, '/api/v1/lots/synthetic/images');
          expect(
            request.headers.value('Authorization'),
            'Bearer synthetic-token',
          );
          final body = await latin1.decoder.bind(request).join();
          expect(body, contains('name="ExpectedVersion"'));
          expect(body, contains('\r\n7\r\n'));
          expect(body, contains('content-type: image/${png ? 'png' : 'jpeg'}'));
          expect(body, contains('name="File"'));
          expect(body, contains('filename="image.${png ? 'png' : 'jpg'}"'));
          request.response
            ..statusCode = 201
            ..write('{"id":"synthetic","version":8}');
          await request.response.close();
        });
        final result = await api.uploadImage('synthetic', file.path, 7);
        await received;
        expect(result['version'], 8);
      } finally {
        await server.close(force: true);
        await directory.delete(recursive: true);
      }
    });
  }
}
