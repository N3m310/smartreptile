import '../data/api_client.dart';
import '../models/species_profile.dart';

/// Service communicating with the species-profile endpoints (/api/v1/species-profiles).
///
/// Follows the architecture where screens/providers use services to talk to ApiClient.
class SpeciesProfileService {
  SpeciesProfileService({required this.api});

  final ApiClient api;

  static const String basePath = '/api/v1/species-profiles';

  /// 1. GET /api/v1/species-profiles
  /// Fetches the catalog of species profiles (both built-in and custom).
  Future<List<SpeciesProfileView>> listProfiles() async {
    final response = await api.getJson(basePath);
    final rawItems = response['items'] as List<dynamic>? ?? const [];
    return rawItems
        .whereType<Map<String, dynamic>>()
        .map(SpeciesProfileView.fromJson)
        .toList();
  }

  /// 2. POST /api/v1/species-profiles
  /// Creates a new custom species profile. Returns the created profile view.
  Future<SpeciesProfileView> createProfile(
    CreateSpeciesProfileRequest request,
  ) async {
    final response = await api.postJson(basePath, body: request.toJson());
    return SpeciesProfileView.fromJson(response);
  }

  /// 3. POST /api/v1/species-profiles/{profileId}/duplicate
  /// Clones an existing profile (built-in or custom) into a new custom profile.
  Future<SpeciesProfileView> duplicateProfile(
    String profileId, {
    String? name,
  }) async {
    final request = DuplicateSpeciesProfileRequest(name: name);
    final response = await api.postJson(
      '$basePath/$profileId/duplicate',
      body: request.toJson(),
    );
    return SpeciesProfileView.fromJson(response);
  }

  /// 4. PATCH /api/v1/species-profiles/{profileId}
  /// Updates mutable fields of an account's custom profile.
  Future<SpeciesProfileView> updateProfile(
    String profileId,
    UpdateSpeciesProfileRequest request,
  ) async {
    final response = await api.patchJson(
      '$basePath/$profileId',
      body: request.toJson(),
    );
    return SpeciesProfileView.fromJson(response);
  }

  /// 5. DELETE /api/v1/species-profiles/{profileId}
  /// Removes a custom profile. Fails with 409 profile_in_use if any terrarium references it.
  Future<void> deleteProfile(String profileId) async {
    await api.deleteJson('$basePath/$profileId');
  }

  /// 6. POST /api/v1/species-profiles/{profileId}/image
  /// Uploads or replaces profile picture. Returns the updated profile view.
  Future<SpeciesProfileView> uploadImage(
    String profileId, {
    required List<int> fileBytes,
    required String filename,
  }) async {
    final response = await api.postMultipart(
      '$basePath/$profileId/image',
      fieldName: 'file',
      fileBytes: fileBytes,
      filename: filename,
    );
    return SpeciesProfileView.fromJson(response);
  }

  /// 7. GET /api/v1/species-profiles/{profileId}/image
  /// Downloads raw image bytes for the profile picture (requires authentication).
  Future<List<int>> getImageBytes(String profileId) {
    return api.getBytes('$basePath/$profileId/image');
  }

  /// 8. DELETE /api/v1/species-profiles/{profileId}/image
  /// Removes the custom profile's picture. Returns updated profile view.
  Future<SpeciesProfileView> deleteImage(String profileId) async {
    final response = await api.deleteJson('$basePath/$profileId/image');
    return SpeciesProfileView.fromJson(response);
  }
}
