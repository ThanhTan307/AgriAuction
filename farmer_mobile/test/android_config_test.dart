import 'package:flutter_test/flutter_test.dart';
import 'package:farmer_mobile/core/config/app_config.dart';

void main() {
  test('USB phone uses reverse localhost; emulator has a separate origin', () {
    expect(
      AppConfig.forTarget(' http://127.0.0.1:5083/ ', 'usb').baseUrl,
      'http://127.0.0.1:5083',
    );
    expect(
      AppConfig.forTarget('http://10.0.2.2:5083', 'emulator').baseUrl,
      'http://10.0.2.2:5083',
    );
    expect(
      () => AppConfig.forTarget('http://10.0.2.2:5083', 'usb'),
      throwsFormatException,
    );
    expect(
      () => AppConfig.forTarget('http://10.0.2.2:5083', 'lan'),
      throwsFormatException,
    );
  });
  test(
    'LAN accepts explicit server origin and rejects database URL or credentials',
    () {
      expect(
        AppConfig.forTarget('https://demo.example.invalid:8443', 'lan').baseUrl,
        'https://demo.example.invalid:8443',
      );
      for (final value in [
        'postgres://server/db',
        'http://user:secret@server',
        'http://server/api',
        'http://server?token=x',
        'http://127.0.0.1:5083',
      ]) {
        expect(() => AppConfig.forTarget(value, 'lan'), throwsFormatException);
      }
    },
  );
}
