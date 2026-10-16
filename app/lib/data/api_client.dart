import 'dart:convert';

import 'package:http/http.dart' as http;

import '../core/problem.dart';

/// The only place in the app that knows the API base URL, the auth header and the problem+json shape.
///
/// Screens never touch this class directly; providers do (`03-implementation/02` §3).
class ApiClient {
  ApiClient({required this.baseUrl, http.Client? httpClient})
    : _http = httpClient ?? http.Client();

  final String baseUrl;
  final http.Client _http;

  /// Access token supplied by `SessionProvider` once the keeper signs in (M2).
  String? accessToken;

  /// Called when a request is refused with 401; the hook renews [accessToken] and answers whether it succeeded, and
  /// the request is then sent once more.
  ///
  /// Wired by `SessionProvider` rather than by each screen, because the access token lives fifteen minutes and every
  /// call would otherwise have to notice the expiry itself.
  Future<bool> Function()? onUnauthorized;

  /// GET that returns a decoded JSON object.
  Future<Map<String, dynamic>> getJson(String path) => _send('GET', path);

  /// POST with an optional JSON body.
  Future<Map<String, dynamic>> postJson(String path, {Object? body}) =>
      _send('POST', path, body);

  /// PATCH with an optional JSON body.
  Future<Map<String, dynamic>> patchJson(String path, {Object? body}) =>
      _send('PATCH', path, body);

  /// PUT with an optional JSON body.
  Future<Map<String, dynamic>> putJson(String path, {Object? body}) =>
      _send('PUT', path, body);

  /// DELETE. The account delete carries `purge` in the query string, so no body is needed here.
  Future<Map<String, dynamic>> deleteJson(String path) => _send('DELETE', path);

  /// Closes the underlying HTTP client.
  void dispose() => _http.close();

  /// Builds a query string, dropping a parameter that is absent or empty.
  ///
  /// A cleared search box has to send *no* `search` at all: `?search=` and an omitted filter mean the same thing to
  /// the server today, and the client should not depend on that staying true.
  static String query(Map<String, Object?> parameters) {
    final pairs = <String>[];

    parameters.forEach((key, value) {
      if (value == null) {
        return;
      }

      final text = value.toString();
      if (text.isEmpty) {
        return;
      }

      pairs.add(
        '${Uri.encodeQueryComponent(key)}=${Uri.encodeQueryComponent(text)}',
      );
    });

    return pairs.isEmpty ? '' : '?${pairs.join('&')}';
  }

  Future<Map<String, dynamic>> _send(
    String method,
    String path, [
    Object? body,
  ]) => _sendOnce(method, path, body, isRetry: false);

  Future<Map<String, dynamic>> _sendOnce(
    String method,
    String path,
    Object? body, {
    required bool isRetry,
  }) async {
    final uri = Uri.parse('$baseUrl$path');
    final headers = _headers(hasBody: body != null);
    final encoded = body == null ? null : jsonEncode(body);

    http.Response response;
    try {
      response = await switch (method) {
        'POST' => _http.post(uri, headers: headers, body: encoded),
        'PATCH' => _http.patch(uri, headers: headers, body: encoded),
        'PUT' => _http.put(uri, headers: headers, body: encoded),
        'DELETE' => _http.delete(uri, headers: headers, body: encoded),
        _ => _http.get(uri, headers: headers),
      }.timeout(const Duration(seconds: 10));
    } on Exception {
      // No status code: the server was not reachable. The UI shows cached values with explicit timestamps
      // rather than pretending they are live (UC-02 A1).
      throw const AppFailure(code: 'network_unreachable', statusCode: 0);
    }

    // Once, and only once: a renewed token that is still refused means the session is over, and retrying would turn
    // that into a loop.
    if (response.statusCode == 401 && !isRetry) {
      final renewed = await onUnauthorized?.call() ?? false;
      if (renewed) {
        return _sendOnce(method, path, body, isRetry: true);
      }
    }

    return _decode(response);
  }

  Map<String, String> _headers({bool hasBody = false}) => {
    'Accept': 'application/json',
    if (hasBody) 'Content-Type': 'application/json',
    if (accessToken != null) 'Authorization': 'Bearer $accessToken',
  };

  Map<String, dynamic> _decode(http.Response response) {
    if (response.statusCode >= 200 && response.statusCode < 300) {
      if (response.body.isEmpty) {
        return const {};
      }
      final decoded = jsonDecode(response.body);
      return decoded is Map<String, dynamic> ? decoded : {'data': decoded};
    }

    // RFC 7807 problem body. Fall back to the status code when the body is not JSON.
    try {
      final problem = jsonDecode(response.body) as Map<String, dynamic>;
      throw AppFailure(
        code: (problem['code'] as String?) ?? 'http_${response.statusCode}',
        statusCode: response.statusCode,
        title: problem['title'] as String?,
        detail: problem['detail'] as String?,
        traceId: problem['traceId'] as String?,
      );
    } on FormatException {
      throw AppFailure(
        code: 'http_${response.statusCode}',
        statusCode: response.statusCode,
      );
    }
  }
}
