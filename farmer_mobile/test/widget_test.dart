import 'package:flutter_test/flutter_test.dart';

import 'package:farmer_mobile/core/config/app_config.dart';
import 'package:farmer_mobile/core/network/api_client.dart';

void main() {
  test('the default API target is configurable for Android emulator use', () {
    const config = AppConfig(baseUrl: 'http://10.0.2.2:5080');
    expect(ApiClient(config).config.baseUrl, 'http://10.0.2.2:5080');
  });
}
