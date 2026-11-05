import 'package:flutter/material.dart';

import 'app.dart';
import 'core/config/app_config.dart';
import 'core/network/api_client.dart';
import 'features/auth/auth_repository.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  final api = ApiClient(AppConfig.fromEnvironment());
  final auth = AuthRepository(api);
  runApp(AgriAuctionApp(api: api, auth: auth));
}
