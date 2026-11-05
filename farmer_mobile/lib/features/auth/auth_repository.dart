import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../../core/network/api_client.dart';
import 'models.dart';

class AuthRepository {
  AuthRepository(this.api);
  final ApiClient api;
  final _storage = const FlutterSecureStorage();
  static const _tokenKey = 'agriauction.mobile.accessToken';

  Future<User?> restore() async {
    final token = await _storage.read(key: _tokenKey);
    if (token == null) return null;
    api.setToken(token);
    try {
      return User.fromJson(
        Map<String, dynamic>.from(await api.get('/api/v1/me')),
      );
    } on ApiException catch (error) {
      if (error.statusCode == 401) await logout();
      rethrow;
    }
  }

  Future<User> login(String email, String password) async {
    final result = Map<String, dynamic>.from(
      await api.post('/api/v1/auth/login', {
        'email': email,
        'password': password,
      }),
    );
    final token = result['accessToken']?.toString();
    final user = User.fromJson(
      Map<String, dynamic>.from(result['user'] as Map),
    );
    if (token == null || user.role != 'Farmer') {
      throw const ApiException(
        403,
        'Tài khoản này không có quyền Farmer trên ứng dụng di động.',
      );
    }
    await _storage.write(key: _tokenKey, value: token);
    api.setToken(token);
    return user;
  }

  Future<void> logout() async {
    try {
      if (apiTokenPresent) await api.post('/api/v1/auth/logout');
    } on ApiException {
      // Local credentials must still be removed when the server session expired.
    } finally {
      await _storage.delete(key: _tokenKey);
      api.setToken(null);
    }
  }

  Future<void> clearLocalSession() async {
    await _storage.delete(key: _tokenKey);
    api.setToken(null);
  }

  bool get apiTokenPresent => api.hasToken;
}
