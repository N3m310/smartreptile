import 'dart:convert';
import 'dart:io';

/// A stub of the backend, running **inside the test process on the device**.
///
/// `main()` builds `ApiClient(baseUrl: Env.apiBase)` itself, so a test cannot inject a mock client. What it can do
/// is bind a real loopback server and point the compile-time `API_BASE` at it: `integration_test` runs the app and
/// the test in the same process, so `127.0.0.1:<port>` resolves to this server from both sides.
///
/// The port is fixed because `Env.apiBase` is compile-time — the run must pass
/// `--dart-define=API_BASE=http://127.0.0.1:18999`, and `scripts/e2e.ps1` does that for the `hermetic` suite.
/// One run at a time: a second concurrent suite would fail to bind, which is better than two suites sharing a stub.
class FakeApi {
  FakeApi._(this._server);

  /// The port every `hermetic` run uses.
  static const int port = 18999;

  /// The base URL the build must be pointed at.
  static String get baseUrl => 'http://127.0.0.1:$port';

  final HttpServer _server;
  final Map<String, _Route> _routes = {};
  final List<String> _unmatched = [];

  /// Requests that reached the stub with no handler — every suite asserts this is empty.
  List<String> get unmatched => List.unmodifiable(_unmatched);

  /// Starts the stub. Fails loudly when the port is taken instead of silently answering from nowhere.
  static Future<FakeApi> start() async {
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, port);
    final api = FakeApi._(server);
    server.listen(api._handle);
    return api;
  }

  /// Answers `METHOD /path` with [reply]. [reply] receives the decoded body and returns the JSON reply body.
  ///
  /// Returning a [StubReply] instead of a bare map allows a specific status code — which is the point: a refusal is
  /// how the app is made to render an error, and `TC-W-02` is about exactly that.
  void on(
    String method,
    String path,
    Object? Function(Map<String, dynamic> body) reply,
  ) {
    _routes['$method $path'] = (body) {
      final result = reply(body);
      return result is StubReply ? result : StubReply.ok(result);
    };
  }

  /// Convenience for the read endpoints the shell calls on sign-in: `{"items": []}` with 200.
  void onEmptyList(String path) =>
      on('GET', path, (_) => <String, dynamic>{'items': []});

  /// How many times `METHOD /path` was called — lets a case prove "no request was made" (§6 `E2E-AI-02`).
  int callsTo(String method, String path) => _counts['$method $path'] ?? 0;

  final Map<String, int> _counts = {};

  Future<void> _handle(HttpRequest request) async {
    final key = '${request.method} ${request.uri.path}';
    _counts[key] = (_counts[key] ?? 0) + 1;

    final raw = await utf8.decoder.bind(request).join();
    Map<String, dynamic> body = const {};
    if (raw.isNotEmpty) {
      final decoded = jsonDecode(raw);
      if (decoded is Map<String, dynamic>) body = decoded;
    }

    final route = _routes[key];
    final reply = route == null
        ? StubReply(404, {'code': 'http_404', 'detail': 'no stub for $key'})
        : route(body);
    if (route == null) _unmatched.add(key);

    final response = request.response
      ..statusCode = reply.status
      ..headers.contentType = ContentType.json
      ..write(jsonEncode(reply.body));
    await response.close();
  }

  /// Stops the server. Called from `tearDownAll`.
  Future<void> stop() => _server.close(force: true);
}

/// A stubbed response: a status code and a JSON body.
class StubReply {
  /// Creates a reply.
  const StubReply(this.status, this.body);

  /// A `200` with [body].
  const StubReply.ok(Object? body) : this(200, body);

  /// Status code to answer with.
  final int status;

  /// JSON body to answer with.
  final Object? body;
}

typedef _Route = StubReply Function(Map<String, dynamic> body);
