import 'package:flutter/foundation.dart';
import 'package:flutter/painting.dart';

import '../../core/network/api_client.dart';
import 'auth_repository.dart';
import 'models.dart';

class AuthState extends ChangeNotifier {
  AuthState(this.repository) {
    repository.api.onUnauthorized = () async {
      await repository.clearLocalSession();
      if (user != null) {
        _forgetUser();
      }
    };
  }
  final AuthRepository repository;
  User? user;
  bool isLoading = true;
  String? error;

  Future<void> restore() async {
    try {
      user = await repository.restore();
    } on ApiException catch (e) {
      if (e.statusCode != 401 && e.statusCode != 403) error = e.message;
    } finally {
      isLoading = false;
      notifyListeners();
    }
  }

  Future<bool> login(String email, String password) async {
    isLoading = true;
    error = null;
    notifyListeners();
    try {
      user = await repository.login(email, password);
      return true;
    } on ApiException catch (e) {
      error = e.message;
      return false;
    } finally {
      isLoading = false;
      notifyListeners();
    }
  }

  Future<void> logout() async {
    await repository.logout();
    _forgetUser();
  }

  void _forgetUser() {
    user = null;
    PaintingBinding.instance.imageCache
      ..clear()
      ..clearLiveImages();
    notifyListeners();
  }
}
