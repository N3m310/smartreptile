import 'package:flutter/foundation.dart';

import '../core/problem.dart';
import '../models/species_profile.dart';
import '../services/species_profile_service.dart';

/// Provider managing species profiles state and operations.
class SpeciesProfileProvider extends ChangeNotifier {
  SpeciesProfileProvider({required this.service});

  final SpeciesProfileService service;

  List<SpeciesProfileView> _profiles = [];
  bool _isLoading = false;
  bool _isSaving = false;
  String? _errorMessage;
  AppFailure? _failure;

  String _searchQuery = '';
  String? _selectedClimateZone;

  // Cache for profile image bytes to prevent repeated network requests
  final Map<String, List<int>> _imageCache = {};

  // ================= GETTERS =================

  /// All fetched profiles.
  List<SpeciesProfileView> get profiles => List.unmodifiable(_profiles);

  /// Filtered profiles according to search query and climate zone filter.
  List<SpeciesProfileView> get filteredProfiles {
    return _profiles.where((profile) {
      if (_selectedClimateZone != null &&
          _selectedClimateZone!.isNotEmpty &&
          profile.climateZone != _selectedClimateZone) {
        return false;
      }

      if (_searchQuery.trim().isNotEmpty) {
        final query = _searchQuery.trim().toLowerCase();
        final matchesName = profile.name.toLowerCase().contains(query);
        final matchesScientific = profile.scientificName.toLowerCase().contains(
          query,
        );
        if (!matchesName && !matchesScientific) {
          return false;
        }
      }

      return true;
    }).toList();
  }

  /// Built-in system profiles.
  List<SpeciesProfileView> get builtInProfiles =>
      _profiles.where((p) => p.isBuiltIn).toList();

  /// User-created custom profiles.
  List<SpeciesProfileView> get customProfiles =>
      _profiles.where((p) => !p.isBuiltIn).toList();

  bool get isLoading => _isLoading;
  bool get isSaving => _isSaving;
  String? get errorMessage => _errorMessage;
  AppFailure? get failure => _failure;
  String get searchQuery => _searchQuery;
  String? get selectedClimateZone => _selectedClimateZone;

  SpeciesProfileView? findById(String id) {
    try {
      return _profiles.firstWhere((p) => p.id == id);
    } catch (_) {
      return null;
    }
  }

  // ================= FILTERS =================

  void setSearchQuery(String query) {
    _searchQuery = query;
    notifyListeners();
  }

  void setClimateZoneFilter(String? zone) {
    _selectedClimateZone = (zone == null || zone.isEmpty) ? null : zone;
    notifyListeners();
  }

  void clearFilters() {
    _searchQuery = '';
    _selectedClimateZone = null;
    notifyListeners();
  }

  // ================= API OPERATIONS =================

  /// Fetches the catalog of profiles from the server.
  Future<void> fetchProfiles({bool silent = false}) async {
    if (!silent) {
      _isLoading = true;
      _errorMessage = null;
      _failure = null;
      notifyListeners();
    }

    try {
      final items = await service.listProfiles();
      _profiles = items;
      _errorMessage = null;
      _failure = null;
    } on AppFailure catch (e) {
      _failure = e;
      _errorMessage = translateError(e);
    } catch (e) {
      _errorMessage = 'Lỗi không xác định khi tải danh mục loài: $e';
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  /// Creates a new custom species profile.
  Future<SpeciesProfileView?> createProfile(
    CreateSpeciesProfileRequest request,
  ) async {
    return _mutate(() async {
      final newProfile = await service.createProfile(request);
      _profiles = [..._profiles, newProfile];
      return newProfile;
    });
  }

  /// Clones a profile into a custom profile owned by caller.
  Future<SpeciesProfileView?> duplicateProfile(
    String profileId, {
    String? name,
  }) async {
    return _mutate(() async {
      final duplicated = await service.duplicateProfile(profileId, name: name);
      _profiles = [..._profiles, duplicated];
      return duplicated;
    });
  }

  /// Updates an existing custom profile.
  Future<SpeciesProfileView?> updateProfile(
    String profileId,
    UpdateSpeciesProfileRequest request,
  ) async {
    return _mutate(() async {
      final updated = await service.updateProfile(profileId, request);
      _profiles = [
        for (final p in _profiles)
          if (p.id == profileId) updated else p,
      ];
      return updated;
    });
  }

  /// Deletes a custom profile.
  Future<bool> deleteProfile(String profileId) async {
    _isSaving = true;
    _errorMessage = null;
    _failure = null;
    notifyListeners();

    try {
      await service.deleteProfile(profileId);
      _profiles = _profiles.where((p) => p.id != profileId).toList();
      _imageCache.remove(profileId);
      return true;
    } on AppFailure catch (e) {
      _failure = e;
      _errorMessage = translateError(e);
      return false;
    } catch (e) {
      _errorMessage = 'Lỗi không xác định khi xóa hồ sơ: $e';
      return false;
    } finally {
      _isSaving = false;
      notifyListeners();
    }
  }

  /// Uploads a picture for a profile.
  Future<SpeciesProfileView?> uploadImage(
    String profileId, {
    required List<int> fileBytes,
    required String filename,
  }) async {
    return _mutate(() async {
      final updated = await service.uploadImage(
        profileId,
        fileBytes: fileBytes,
        filename: filename,
      );
      _imageCache[profileId] = fileBytes;
      _profiles = [
        for (final p in _profiles)
          if (p.id == profileId) updated else p,
      ];
      return updated;
    });
  }

  /// Fetches cached or downloaded image bytes.
  Future<List<int>?> getImageBytes(String profileId) async {
    if (_imageCache.containsKey(profileId)) {
      return _imageCache[profileId];
    }
    try {
      final bytes = await service.getImageBytes(profileId);
      _imageCache[profileId] = bytes;
      return bytes;
    } catch (_) {
      return null;
    }
  }

  /// Deletes the picture of a profile.
  Future<SpeciesProfileView?> deleteImage(String profileId) async {
    return _mutate(() async {
      final updated = await service.deleteImage(profileId);
      _imageCache.remove(profileId);
      _profiles = [
        for (final p in _profiles)
          if (p.id == profileId) updated else p,
      ];
      return updated;
    });
  }

  /// Clears cache and state upon sign out.
  void clear() {
    _profiles = [];
    _imageCache.clear();
    _isLoading = false;
    _isSaving = false;
    _errorMessage = null;
    _failure = null;
    _searchQuery = '';
    _selectedClimateZone = null;
    notifyListeners();
  }

  // ================= HELPERS =================

  Future<T?> _mutate<T>(Future<T> Function() action) async {
    _isSaving = true;
    _errorMessage = null;
    _failure = null;
    notifyListeners();

    try {
      final result = await action();
      return result;
    } on AppFailure catch (e) {
      _failure = e;
      _errorMessage = translateError(e);
      return null;
    } catch (e) {
      _errorMessage = 'Thao tác thất bại: $e';
      return null;
    } finally {
      _isSaving = false;
      notifyListeners();
    }
  }

  /// Translates backend error codes into friendly Vietnamese messages.
  static String translateError(AppFailure failure) {
    if (failure.isNetworkFailure) {
      return 'Không thể kết nối đến máy chủ. Vui lòng kiểm tra lại mạng.';
    }

    switch (failure.code) {
      case 'builtin_immutable':
        return 'Hồ sơ loài mặc định của hệ thống không thể chỉnh sửa hoặc xóa. Bạn có thể chọn "Nhân bản" để tạo bản sao và tùy chỉnh.';
      case 'profile_name_taken':
        return 'Tên hồ sơ loài này đã được bạn sử dụng. Vui lòng đặt một tên khác.';
      case 'profile_in_use':
        return 'Không thể xóa hồ sơ loài vì hiện đang có bể nuôi (hoặc lịch sử) sử dụng hồ sơ này.';
      case 'not_found':
        return 'Không tìm thấy hồ sơ loài yêu cầu.';
      case 'image_not_set':
        return 'Hồ sơ này chưa có ảnh đại diện.';
      case 'image_required':
        return 'Vui lòng chọn một tập tin ảnh hợp lệ (PNG, JPEG, WebP tối đa 2 MB).';
      case 'unauthenticated':
        return 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.';
      case 'errorForbidden':
        return 'Bạn không có quyền thực hiện thao tác này (cần quyền species.write).';
      default:
        if (failure.detail != null && failure.detail!.isNotEmpty) {
          return failure.detail!;
        }
        if (failure.title != null && failure.title!.isNotEmpty) {
          return failure.title!;
        }
        return 'Đã xảy ra lỗi (${failure.code}). Mã trạng thái: ${failure.statusCode}.';
    }
  }
}
