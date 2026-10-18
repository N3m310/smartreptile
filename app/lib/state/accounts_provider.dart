import 'package:flutter/foundation.dart';

import '../core/paging.dart';
import '../core/problem.dart';
import '../data/api_client.dart';
import '../models/admin_models.dart';

/// Account administration: `/api/v1/users` (FR-18).
///
/// Reads need `user.read` and every write needs `user.manage` — the server enforces both, and the screens hide the
/// actions they may not perform rather than letting a button fail. A mutation answers with an [AppFailure] instead
/// of throwing, so a dialog can show the refusal ("that is the last administrator") next to the field it belongs to
/// rather than as an unhandled exception.
class AccountsProvider extends ChangeNotifier {
  /// Creates the provider.
  AccountsProvider({required this._api});

  final ApiClient _api;

  PagedState<AccountSummary>? _accounts;
  bool _isSaving = false;

  /// The accounts page, or null while it has never been read.
  PagedState<AccountSummary>? get accounts => _accounts;

  /// True while a write is in flight, so a dialog can disable its own submit button.
  bool get isSaving => _isSaving;

  /// Reads one page of accounts.
  Future<void> loadAccounts({String? search, int skip = 0}) async {
    final filter = search ?? _accounts?.search ?? '';
    _accounts = (_accounts ??
            PagedState<AccountSummary>(
              items: const [],
              total: 0,
              skip: 0,
              take: adminPageSize,
              search: filter,
            ))
        .copyWith(search: filter, isLoading: true, clearFailure: true);
    notifyListeners();

    try {
      final payload = await _api.getJson(
        '/api/v1/users${ApiClient.query({'search': filter, 'skip': skip, 'take': adminPageSize})}',
      );
      final page = ApiPage.fromJson(payload, AccountSummary.fromJson);

      _accounts = _accounts!.copyWith(
        items: page.items,
        total: page.total,
        skip: page.skip,
        take: page.take,
        isLoading: false,
        clearFailure: true,
      );
    } on AppFailure catch (failure) {
      _accounts = _accounts!.copyWith(isLoading: false, failure: failure);
    }

    notifyListeners();
  }

  /// Creates an account. Answers null on success, or the refusal to show.
  Future<AppFailure?> createAccount({
    required String username,
    required String email,
    required String password,
    required String role,
    required String preferredLanguage,
  }) => _mutate(
    () => _api.postJson(
      '/api/v1/users',
      body: {
        'username': username,
        'email': email,
        'password': password,
        'role': role,
        'preferredLanguage': preferredLanguage,
      },
    ),
  );

  /// Edits an account. An omitted member keeps its value, which is how the server reads the patch.
  Future<AppFailure?> updateAccount(
    String userId, {
    String? username,
    String? email,
    String? preferredLanguage,
    String? minNotifySeverity,
    String? quietHoursStart,
    String? quietHoursEnd,
  }) => _mutate(
    () => _api.patchJson(
      '/api/v1/users/$userId',
      body: {
        'username': username,
        'email': email,
        'preferredLanguage': preferredLanguage,
        'minNotifySeverity': minNotifySeverity,
        'quietHoursStart': quietHoursStart,
        'quietHoursEnd': quietHoursEnd,
      },
    ),
  );

  /// Replaces an account's password; every session of that account ends.
  Future<AppFailure?> resetPassword(String userId, String newPassword) => _mutate(
    () => _api.postJson(
      '/api/v1/users/$userId/reset-password',
      body: {'newPassword': newPassword},
    ),
  );

  /// Moves an account onto another role, by role code.
  Future<AppFailure?> changeRole(String userId, String role) => _mutate(
    () => _api.patchJson('/api/v1/users/$userId/role', body: {'role': role}),
  );

  /// Disables or re-enables an account.
  Future<AppFailure?> setDisabled(String userId, {required bool disabled}) =>
      _mutate(
        () => _api.postJson('/api/v1/users/$userId/${disabled ? 'disable' : 'enable'}'),
      );

  /// Removes an account.
  ///
  /// [purge] false is the soft delete: the account is disabled and every row that names it is kept. True erases the
  /// row and is allowed only while nothing references it, which is why it is offered second in the dialog.
  Future<AppFailure?> deleteAccount(String userId, {required bool purge}) =>
      _mutate(
        () => _api.deleteJson(
          '/api/v1/users/$userId${ApiClient.query({'purge': purge})}',
        ),
      );

  /// Drops everything, so a sign-out cannot leave the deployment's accounts on screen.
  void clear() {
    _accounts = null;
    _isSaving = false;
    notifyListeners();
  }

  /// Runs a write and reloads the page it changed.
  Future<AppFailure?> _mutate(Future<Map<String, dynamic>> Function() request) async {
    _isSaving = true;
    notifyListeners();

    try {
      await request();
      await loadAccounts(skip: _accounts?.skip ?? 0);
      return null;
    } on AppFailure catch (failure) {
      return failure;
    } finally {
      _isSaving = false;
      notifyListeners();
    }
  }
}
