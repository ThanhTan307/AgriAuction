import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'api_client.dart';

/// Invalidation only. Callers fetch protected snapshots again after each event.
class LiveUpdates {
  LiveUpdates(
    this.api,
    this.path,
    this.target,
    this.refresh, {
    this.joinMethod,
    this.joinArguments = const [],
  });
  final ApiClient api;
  final String path, target;
  final void Function() refresh;
  final String? joinMethod;
  final List<Object> joinArguments;
  WebSocket? _socket;
  Timer? _retry, _ping, _connectTimeout;
  bool _disposed = false, _connecting = false;

  Future<void> connect() async {
    if (_disposed || _connecting) return;
    _connecting = true;
    try {
      final base = Uri.parse(api.config.baseUrl);
      final connectFuture = WebSocket.connect(
        base
            .replace(scheme: base.scheme == 'https' ? 'wss' : 'ws', path: path)
            .toString(),
        headers: api.authHeaders,
      );
      final completer = Completer<WebSocket>();
      _connectTimeout = Timer(const Duration(seconds: 10), () {
        if (!completer.isCompleted) {
          completer.completeError(
            TimeoutException('WebSocket connection timed out', const Duration(seconds: 10)),
          );
        }
      });
      connectFuture.then((sock) {
        if (!completer.isCompleted) completer.complete(sock);
      }, onError: (e, st) {
        if (!completer.isCompleted) completer.completeError(e, st);
      });
      final socket = await completer.future;
      _connectTimeout?.cancel();
      if (_disposed) {
        await socket.close();
        return;
      }
      _socket = socket;
      var buffer = '';
      var handshaken = false;
      socket.add('${jsonEncode({'protocol': 'json', 'version': 1})}\u001e');
      _ping = Timer.periodic(const Duration(seconds: 10), (_) {
        if (socket.readyState == WebSocket.open) socket.add('{"type":6}\u001e');
      });
      socket.listen(
        (data) {
          buffer += data is String ? data : utf8.decode(data as List<int>);
          if (buffer.length > 65536) {
            _reconnect();
            return;
          }
          final parts = buffer.split('\u001e');
          buffer = parts.removeLast();
          for (final part in parts) {
            if (part.isEmpty) continue;
            try {
              final message = jsonDecode(part);
              if (message is! Map) continue;
              if (!handshaken) {
                if (message['error'] != null) {
                  _reconnect();
                  return;
                }
                handshaken = true;
                if (joinMethod != null) {
                  socket.add(
                    '${jsonEncode({'type': 1, 'invocationId': 'join', 'target': joinMethod, 'arguments': joinArguments})}\u001e',
                  );
                } else {
                  refresh();
                }
              } else if (message['target'] == target ||
                  (message['type'] == 3 &&
                      message['invocationId'] == 'join' &&
                      message['error'] == null)) {
                refresh();
              } else if (message['type'] == 7) {
                _reconnect();
              }
            } catch (_) {
              /* The protected polling request recovers stale state. */
            }
          }
        },
        onError: (_) => _reconnect(),
        onDone: _reconnect,
        cancelOnError: true,
      );
    } catch (_) {
      _reconnect();
    } finally {
      _connecting = false;
    }
  }

  void _reconnect() {
    _connectTimeout?.cancel();
    _ping?.cancel();
    final socket = _socket;
    _socket = null;
    socket?.close();
    _retry?.cancel();
    if (!_disposed) _retry = Timer(const Duration(seconds: 5), connect);
  }

  void dispose() {
    _disposed = true;
    _connectTimeout?.cancel();
    _retry?.cancel();
    _ping?.cancel();
    _socket?.close();
  }
}
