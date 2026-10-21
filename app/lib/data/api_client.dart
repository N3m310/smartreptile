import 'dart:convert';
import 'dart:typed_data';

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

  Future<Uint8List> getBytes(String path, {bool isRetry = false}) async {
    final uri = Uri.parse(path.startsWith('http') ? path : '$baseUrl$path');
    http.Response response;
    try {
      response = await _http
          .get(uri, headers: _headers())
          .timeout(const Duration(seconds: 10));
    } on Exception {
      throw const AppFailure(code: 'network_unreachable', statusCode: 0);
    }
    if (response.statusCode == 401 && !isRetry) {
      final renewed = await onUnauthorized?.call() ?? false;
      if (renewed) {
        return getBytes(path, isRetry: true);
      }
    }
    if (response.statusCode >= 200 && response.statusCode < 300) {
      return response.bodyBytes;
    }
    _decode(response);
    throw AppFailure(
      code: 'http_${response.statusCode}',
      statusCode: response.statusCode,
    );
  }

  /// POST with an optional JSON body.
  Future<Map<String, dynamic>> postJson(String path, {Object? body}) =>
      _send('POST', path, body: body);

  /// PATCH with an optional JSON body.
  Future<Map<String, dynamic>> patchJson(
    String path, {
    Object? body,
    Map<String, String>? headers,
  }) => _send('PATCH', path, body: body, headers: headers);

  /// PUT with an optional JSON body.
  Future<Map<String, dynamic>> putJson(String path, {Object? body}) =>
      _send('PUT', path, body: body);

  /// DELETE. The account delete carries `purge` in the query string, so no body is needed here.
  Future<Map<String, dynamic>> deleteJson(String path) => _send('DELETE', path);

  /// POST a single authenticated file part, retrying once after token renewal.
  Future<Map<String, dynamic>> postMultipart(
    String path, {
    required String fieldName,
    required List<int> fileBytes,
    required String filename,
  }) => _sendMultipart(
    path,
    fieldName: fieldName,
    fileBytes: fileBytes,
    filename: filename,
    isRetry: false,
  );

  Future<Map<String, dynamic>> _sendMultipart(
    String path, {
    required String fieldName,
    required List<int> fileBytes,
    required String filename,
    required bool isRetry,
  }) async {
    final request = http.MultipartRequest('POST', Uri.parse('$baseUrl$path'));
    request.headers.addAll(_headers());
    request.files.add(
      http.MultipartFile.fromBytes(fieldName, fileBytes, filename: filename),
    );

    http.Response response;
    try {
      final streamed = await _http
          .send(request)
          .timeout(const Duration(seconds: 20));
      response = await http.Response.fromStream(streamed);
    } on Exception {
      throw const AppFailure(code: 'network_unreachable', statusCode: 0);
    }

    if (response.statusCode == 401 && !isRetry) {
      final renewed = await onUnauthorized?.call() ?? false;
      if (renewed) {
        return _sendMultipart(
          path,
          fieldName: fieldName,
          fileBytes: fileBytes,
          filename: filename,
          isRetry: true,
        );
      }
    }

    return _decode(response);
  }

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
    String path, {
    Object? body,
    Map<String, String>? headers,
  }) => _sendOnce(method, path, body: body, headers: headers, isRetry: false);

  Future<Map<String, dynamic>> _sendOnce(
    String method,
    String path, {
    Object? body,
    Map<String, String>? headers,
    required bool isRetry,
  }) async {
    final uri = Uri.parse('$baseUrl$path');
    final requestHeaders = {..._headers(hasBody: body != null), ...?headers};
    final encoded = body == null ? null : jsonEncode(body);

    http.Response response;
    try {
      response = await switch (method) {
        'POST' => _http.post(uri, headers: requestHeaders, body: encoded),
        'PATCH' => _http.patch(uri, headers: requestHeaders, body: encoded),
        'PUT' => _http.put(uri, headers: requestHeaders, body: encoded),
        'DELETE' => _http.delete(uri, headers: requestHeaders, body: encoded),
        _ => _http.get(uri, headers: requestHeaders),
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
        return _sendOnce(
          method,
          path,
          body: body,
          headers: headers,
          isRetry: true,
        );
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
      final rawErrors = problem['errors'];
      final errorsList = rawErrors is List ? rawErrors : const [];
      throw AppFailure(
        code: (problem['code'] as String?) ?? 'http_${response.statusCode}',
        statusCode: response.statusCode,
        title: problem['title'] as String?,
        detail: problem['detail'] as String?,
        traceId: problem['traceId'] as String?,
        errors: errorsList,
      );
    } on FormatException {
      throw AppFailure(
        code: 'http_${response.statusCode}',
        statusCode: response.statusCode,
      );
    }
  }
}
