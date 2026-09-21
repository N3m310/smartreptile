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

  /// Access token supplied by `AuthProvider` once login exists (M2).
  String? accessToken;

  /// GET that returns a decoded JSON object.
  Future<Map<String, dynamic>> getJson(String path) async {
    final uri = Uri.parse('$baseUrl$path');

    http.Response response;
    try {
      response = await _http
          .get(uri, headers: _headers())
          .timeout(const Duration(seconds: 10));
    } on Exception {
      // No status code: the server was not reachable. The UI shows cached values with explicit timestamps
      // rather than pretending they are live (UC-02 A1).
      throw const AppFailure(code: 'network_unreachable', statusCode: 0);
    }

    return _decode(response);
  }

  /// Closes the underlying HTTP client.
  void dispose() => _http.close();

  Map<String, String> _headers() => {
    'Accept': 'application/json',
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
