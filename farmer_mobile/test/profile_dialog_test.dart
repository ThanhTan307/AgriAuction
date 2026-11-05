import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:farmer_mobile/core/config/app_config.dart';
import 'package:farmer_mobile/core/network/api_client.dart';
import 'package:farmer_mobile/features/auth/auth_repository.dart';
import 'package:farmer_mobile/features/auth/auth_state.dart';
import 'package:farmer_mobile/features/profile/profile_page.dart';

class ProfileApi extends ApiClient {
  ProfileApi() : super(const AppConfig(baseUrl: 'http://127.0.0.1:1'));
  String name = 'Farmer';
  int writes = 0;
  @override
  Future<dynamic> get(String path) async => {
    'displayName': name,
    'email': 'synthetic@completion.invalid',
    'role': 'Farmer',
    'approvalState': 'Approved',
    'version': 7,
  };
  @override
  Future<dynamic> patch(String path, Object body) async {
    expect(path, '/api/v1/me/profile');
    expect((body as Map)['expectedVersion'], 7);
    writes++;
    name = body['displayName'];
    return {'displayName': name};
  }
}

void main() {
  for (final save in [true, false]) {
    testWidgets('profile dialog closes safely on ${save ? 'save' : 'cancel'}', (
      tester,
    ) async {
      final api = ProfileApi();
      final auth = AuthState(AuthRepository(api));
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: ProfilePage(api: api, authState: auth),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Sửa tên hiển thị'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField), 'Farmer Android');
      await tester.tap(find.text(save ? 'Lưu' : 'Hủy'));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      expect(find.byType(AlertDialog), findsNothing);
      expect(api.writes, save ? 1 : 0);
      if (save) expect(find.text('Farmer Android'), findsOneWidget);
      await tester.pumpWidget(const SizedBox.shrink());
      auth.dispose();
    });
  }
}
