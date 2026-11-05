import 'dart:async';
import 'dart:convert';
import 'dart:io';

/// Which side of a camera link this socket is.
enum CameraSignalRole { camera, viewer }

/// Client for the API's camera signalling socket (`/ws/camera/{terrariumId}`).
///
/// The socket carries WebRTC offer/answer/ICE messages and nothing else; the video itself is peer-to-peer. That is
/// deliberate: relaying a stream through the API would put a terrarium's bandwidth on the server, and the product's
/// whole point is that it keeps working while the frontend is closed.
///
/// The access token travels as a query parameter because a WebSocket handshake cannot carry an Authorization header
/// from a browser — the same reason the API accepts it on `/hubs` (§02-design/06).
class CameraSignaling {
  CameraSignaling({
    required this.baseUrl,
    required this.accessToken,
    required this.terrariumId,
    required this.role,
  });

  /// API base (`Env.apiBase`), `http://` or `https://`.
  final String baseUrl;

  /// Bearer token of the signed-in session.
  final String accessToken;

  /// Room key: the terrarium whose camera is being watched or published.
  final String terrariumId;

  final CameraSignalRole role;

  final StreamController<Map<String, dynamic>> _events =
      StreamController<Map<String, dynamic>>.broadcast();

  WebSocket? _socket;
  bool _closed = false;

  /// The id the server assigned this socket; the peer uses it to address a single viewer.
  String? peerId;

  /// Messages from the server, decoded. Subscription starts before [connect] so nothing is missed.
  Stream<Map<String, dynamic>> get events => _events.stream;

  bool get isConnected => _socket != null;

  /// Opens the socket. Throws when the API refuses (unauthorised, foreign terrarium, upgrade rejected).
  Future<void> connect() async {
    _closed = false;
    final uri = Uri.parse(baseUrl).replace(
      scheme: baseUrl.startsWith('https') ? 'wss' : 'ws',
      path: '/ws/camera/$terrariumId',
      queryParameters: {
        'role': role == CameraSignalRole.camera ? 'camera' : 'viewer',
        'access_token': accessToken,
      },
    );

    final socket = await WebSocket.connect(uri.toString());
    _socket = socket;

    socket.listen(
      (frame) {
        if (frame is! String) return;
        final decoded = jsonDecode(frame);
        if (decoded is! Map<String, dynamic>) return;

        if (decoded['type'] == 'welcome') {
          peerId = decoded['peerId'] as String?;
        }

        if (!_events.isClosed) _events.add(decoded);
      },
      onDone: () {
        _socket = null;
        if (!_closed && !_events.isClosed) {
          _events.add({'type': 'socket-closed'});
        }
      },
      onError: (Object error) {
        if (!_events.isClosed) _events.add({'type': 'socket-error', 'error': '$error'});
      },
      cancelOnError: false,
    );
  }

  /// Sends one signalling message. Silently dropped once the socket is gone: the caller reconnects instead.
  void send(Map<String, dynamic> message) {
    final socket = _socket;
    if (socket == null) return;
    socket.add(jsonEncode(message));
  }

  /// Closes the socket; the server tells the other side the peer left.
  Future<void> close() async {
    _closed = true;
    await _socket?.close(WebSocketStatus.normalClosure);
    _socket = null;
    await _events.close();
  }
}
