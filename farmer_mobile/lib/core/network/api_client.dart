import 'dart:convert';
import 'dart:async';
import 'dart:io';

import 'package:http/http.dart' as http;
import 'package:http_parser/http_parser.dart';

import '../config/app_config.dart';

class ApiException implements Exception {
  const ApiException(
    this.statusCode,
    this.message, {
    this.code,
    this.fieldErrors = const {},
  });
  final int statusCode;
  final String message;
  final String? code;
  final Map<String, String> fieldErrors;
  @override
  String toString() => message;
}

class ApiClient {
  ApiClient(this.config);
  final AppConfig config;
  String? _token;
  Future<void> Function()? onUnauthorized;

  void setToken(String? token) => _token = token;
  bool get hasToken => _token != null;
  Map<String, String> get authHeaders =>
      _token == null ? const {} : {'Authorization': 'Bearer $_token'};

  Future<http.MultipartFile> _imagePart(String field, String path) async {
    // Android's picker may compress to JPEG while retaining a .png filename.
    final input = await File(path).open();
    late List<int> signature;
    try {
      signature = await input.read(8);
    } finally {
      await input.close();
    }
    final png =
        signature.length == 8 &&
        signature[0] == 137 &&
        signature[1] == 80 &&
        signature[2] == 78 &&
        signature[3] == 71 &&
        signature[4] == 13 &&
        signature[5] == 10 &&
        signature[6] == 26 &&
        signature[7] == 10;
    final jpeg =
        signature.length >= 3 &&
        signature[0] == 255 &&
        signature[1] == 216 &&
        signature[2] == 255;
    if (!png && !jpeg) {
      throw const ApiException(400, 'Chỉ chọn ảnh JPEG hoặc PNG hợp lệ.');
    }
    return http.MultipartFile.fromPath(
      field,
      path,
      filename: png ? 'image.png' : 'image.jpg',
      contentType: MediaType('image', png ? 'png' : 'jpeg'),
    );
  }

  Future<dynamic> get(String path) => _send('GET', path);
  Future<dynamic> post(
    String path, [
    Object? body,
    Map<String, String>? headers,
  ]) => _send('POST', path, body: body, headers: headers);
  Future<dynamic> patch(String path, Object body) =>
      _send('PATCH', path, body: body);

  Future<dynamic> _send(
    String method,
    String path, {
    Object? body,
    Map<String, String>? headers,
  }) async {
    final request = http.Request(method, Uri.parse('${config.baseUrl}$path'));
    request.headers.addAll({
      'Accept': 'application/json',
      if (_token != null) 'Authorization': 'Bearer $_token',
      ...?headers,
    });
    if (body != null) {
      request.headers['Content-Type'] = 'application/json';
      request.body = jsonEncode(body);
    }
    final client = http.Client();
    try {
      final response = await client
          .send(request)
          .timeout(const Duration(seconds: 20));
      final text = await response.stream.bytesToString();
      dynamic decoded;
      if (text.isNotEmpty) {
        try {
          decoded = jsonDecode(text);
        } on FormatException {
          decoded = text;
        }
      }
      if (response.statusCode < 200 || response.statusCode >= 300) {
        if (response.statusCode == 401) await onUnauthorized?.call();
        final errors = _fieldErrors(decoded);
        final message = _errorMessage(decoded, response.statusCode, errors);
        throw ApiException(
          response.statusCode,
          message.toString(),
          code: decoded is Map ? decoded['code']?.toString() : null,
          fieldErrors: errors,
        );
      }
      return decoded;
    } on SocketException {
      throw const ApiException(
        0,
        'Không có kết nối mạng. Hãy kiểm tra mạng và thử lại.',
      );
    } on http.ClientException {
      throw const ApiException(
        0,
        'Không thể kết nối máy chủ. Hãy thử lại sau.',
      );
    } on TimeoutException {
      throw const ApiException(
        0,
        'Chưa xác nhận kết quả. Kiểm tra mạng và thử lại cùng yêu cầu.',
      );
    } finally {
      client.close();
    }
  }

  Future<dynamic> multipart(
    String path,
    Map<String, String> fields,
    List<String> files, {
    Map<String, String>? headers,
  }) async {
    final request = http.MultipartRequest(
      'POST',
      Uri.parse('${config.baseUrl}$path'),
    );
    request.headers.addAll({
      ...authHeaders,
      ...?headers,
      'Accept': 'application/json',
    });
    request.fields.addAll(fields);
    for (final file in files) {
      request.files.add(await _imagePart('files', file));
    }
    try {
      final response = await request.send().timeout(
        const Duration(seconds: 30),
      );
      final text = await response.stream.bytesToString();
      final decoded = text.isEmpty ? null : jsonDecode(text);
      if (response.statusCode < 200 || response.statusCode >= 300) {
        if (response.statusCode == 401) await onUnauthorized?.call();
        final errors = _fieldErrors(decoded);
        throw ApiException(
          response.statusCode,
          _errorMessage(decoded, response.statusCode, errors),
          fieldErrors: errors,
        );
      }
      return decoded;
    } on SocketException {
      throw const ApiException(
        0,
        'Chưa xác nhận kết quả. Kiểm tra mạng và thử lại cùng yêu cầu.',
      );
    } on TimeoutException {
      throw const ApiException(
        0,
        'Chưa xác nhận kết quả. Thử lại cùng yêu cầu.',
      );
    }
  }

  Future<Map<String, dynamic>> uploadImage(
    String lotId,
    String filePath,
    int expectedVersion,
  ) async {
    final request = http.MultipartRequest(
      'POST',
      Uri.parse('${config.baseUrl}/api/v1/lots/$lotId/images'),
    );
    request.headers['Accept'] = 'application/json';
    if (_token != null) request.headers['Authorization'] = 'Bearer $_token';
    request.fields['ExpectedVersion'] = expectedVersion.toString();
    request.files.add(await _imagePart('File', filePath));
    try {
      final response = await request.send().timeout(
        const Duration(seconds: 30),
      );
      final text = await response.stream.bytesToString();
      final decoded = text.isEmpty ? <String, dynamic>{} : jsonDecode(text);
      if (response.statusCode < 200 || response.statusCode >= 300) {
        if (response.statusCode == 401) await onUnauthorized?.call();
        final errors = _fieldErrors(decoded);
        throw ApiException(
          response.statusCode,
          _errorMessage(decoded, response.statusCode, errors),
          fieldErrors: errors,
        );
      }
      return Map<String, dynamic>.from(decoded as Map);
    } on SocketException {
      throw const ApiException(0, 'Không có kết nối mạng.');
    }
  }

  static Map<String, String> _fieldErrors(dynamic decoded) {
    if (decoded is! Map || decoded['errors'] is! Map) {
      return const {};
    }
    final result = <String, String>{};
    for (final entry in (decoded['errors'] as Map).entries) {
      final rawKey = entry.key.toString().split('.').last;
      final key = rawKey.isEmpty
          ? rawKey
          : '${rawKey[0].toLowerCase()}${rawKey.substring(1)}';
      final value = entry.value;
      var message = value is List && value.isNotEmpty
          ? value.first.toString()
          : value.toString();
      if (message.contains('could not be converted to System.Decimal')) {
        message =
            'Giá trị phải là số hợp lệ. Có thể nhập 2000000 hoặc 2.000.000.';
      }
      if (message.isNotEmpty) result[key] = message;
    }
    if (result.length > 1) result.remove('request');
    return result;
  }

  static String _errorMessage(
    dynamic decoded,
    int statusCode,
    Map<String, String> fieldErrors,
  ) {
    if (fieldErrors.isNotEmpty) return fieldErrors.values.join(' ');
    if (decoded is Map) {
      final value = decoded['detail'] ?? decoded['title'] ?? decoded['message'];
      if (value is String && value.trim().isNotEmpty) {
        if (value == 'One or more validation errors occurred.') {
          return 'Thông tin chưa hợp lệ. Hãy kiểm tra các trường được đánh dấu.';
        }
        return value;
      }
    }
    return 'Không thể xử lý yêu cầu (HTTP $statusCode).';
  }
}
