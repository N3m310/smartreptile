import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Where the refresh token lives between launches.
///
/// An interface rather than a `FlutterSecureStorage` field, for the same reason `Clock` is one: the session has to be
/// constructible where no keychain exists. A widget test that reaches a real platform channel does not fail — it
/// *waits*, which is worse, because it looks like a slow test until it turns into a ten-minute timeout.
abstract class TokenStore {
  /// The stored refresh token, or null when there is none.
  Future<String?> read();

  /// Stores [token], or clears what is there when it is null or empty.
  Future<void> write(String? token);

  /// The platform keychain — the only place a thirty-day credential may be kept (NFR-04).
  static TokenStore secure() => const SecureTokenStore();

  /// Storage that forgets when the process ends, for a test or a platform without a keychain.
  static TokenStore inMemory([String? token]) => MemoryTokenStore(token);
}

/// Keychain-backed storage.
class SecureTokenStore implements TokenStore {
  /// Creates the store.
  const SecureTokenStore({FlutterSecureStorage? storage})
    : _storage = storage ?? const FlutterSecureStorage();

  static const _key = 'session.refreshToken';

  final FlutterSecureStorage _storage;

  @override
  Future<String?> read() async {
    try {
      return await _storage.read(key: _key);
    } on Exception {
      // No keychain in this environment: the session simply does not survive a restart.
      return null;
    }
  }

  @override
  Future<void> write(String? token) async {
    try {
      if (token == null || token.isEmpty) {
        await _storage.delete(key: _key);
      } else {
        await _storage.write(key: _key, value: token);
      }
    } on Exception {
      // Nothing to do: the token stays in memory for this run.
    }
  }
}

/// In-memory storage. Not a production store: it exists so a test can exercise sign-in and renewal without a
/// keychain, and so a failure in the storage layer cannot be mistaken for a failure in the session.
class MemoryTokenStore implements TokenStore {
  /// Creates the store, optionally already holding [token].
  MemoryTokenStore([this._token]);

  String? _token;

  @override
  Future<String?> read() async => _token;

  @override
  Future<void> write(String? token) async {
    _token = token == null || token.isEmpty ? null : token;
  }
}
