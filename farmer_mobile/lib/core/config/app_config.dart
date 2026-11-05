class AppConfig {
  const AppConfig({required this.baseUrl});

  final String baseUrl;

  factory AppConfig.forTarget(String value, String target) {
    final url = value.trim().replaceFirst(RegExp(r'/+$'), '');
    final uri = Uri.tryParse(url);
    if (!['emulator', 'usb', 'lan'].contains(target) ||
        uri == null ||
        !['http', 'https'].contains(uri.scheme) ||
        uri.host.isEmpty ||
        uri.userInfo.isNotEmpty ||
        uri.hasQuery ||
        uri.hasFragment ||
        (uri.path.isNotEmpty && uri.path != '/')) {
      throw const FormatException(
        'API URL must be an absolute HTTP(S) origin.',
      );
    }
    if (target != 'emulator' && uri.host == '10.0.2.2') {
      throw const FormatException('10.0.2.2 is only for the Android emulator.');
    }
    if (target == 'usb' && uri.host != '127.0.0.1') {
      throw const FormatException(
        'USB profile requires adb reverse and 127.0.0.1.',
      );
    }
    if (target == 'lan' &&
        ['127.0.0.1', 'localhost', '::1'].contains(uri.host)) {
      throw const FormatException(
        'LAN profile requires a reachable server address.',
      );
    }
    return AppConfig(baseUrl: url);
  }

  factory AppConfig.fromEnvironment() {
    const configured = String.fromEnvironment(
      'API_BASE_URL',
      defaultValue: 'http://10.0.2.2:5084',
    );
    const target = String.fromEnvironment(
      'ANDROID_TARGET',
      defaultValue: 'emulator',
    );
    return AppConfig.forTarget(configured, target);
  }
}
