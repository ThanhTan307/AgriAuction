import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:farmer_mobile/app.dart';
import 'package:farmer_mobile/core/config/app_config.dart';
import 'package:farmer_mobile/core/network/api_client.dart';
import 'package:farmer_mobile/features/auth/auth_repository.dart';
import 'package:farmer_mobile/features/auth/login_page.dart';
import 'package:farmer_mobile/features/auth/models.dart';
import 'package:farmer_mobile/features/home/home_page.dart';

class FakeApi extends ApiClient {
  FakeApi() : super(const AppConfig(baseUrl: 'http://127.0.0.1:1'));
  @override
  Future<dynamic> get(String path) async => {
    'items': [],
    'total': 0,
    'page': 1,
    'pageSize': 20,
  };
}

class FakeAuth extends AuthRepository {
  FakeAuth(super.api);
  @override
  Future<User?> restore() async => const User(
    id: 'test-farmer',
    email: 'synthetic@completion.invalid',
    displayName: 'Farmer',
    role: 'Farmer',
  );
  @override
  Future<void> clearLocalSession() async {}
}

void main() {
  testWidgets(
    'revocation removes a pushed private screen and returns to login',
    (tester) async {
      final api = FakeApi();
      await tester.pumpWidget(AgriAuctionApp(api: api, auth: FakeAuth(api)));
      await tester.pumpAndSettle();
      expect(find.byType(HomePage), findsOneWidget);
      final context = tester.element(find.byType(HomePage));
      Navigator.of(context).push(
        MaterialPageRoute<void>(
          builder: (_) =>
              const Scaffold(body: Text('Private transaction details')),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Private transaction details'), findsOneWidget);
      await api.onUnauthorized!();
      await tester.pumpAndSettle();
      expect(find.text('Private transaction details'), findsNothing);
      expect(find.byType(LoginPage), findsOneWidget);
      expect(
        Navigator.of(tester.element(find.byType(LoginPage))).canPop(),
        isFalse,
      );
    },
  );
}
