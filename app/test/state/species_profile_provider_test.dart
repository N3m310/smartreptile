import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:smart_reptile/data/api_client.dart';
import 'package:smart_reptile/models/species_profile.dart';
import 'package:smart_reptile/services/species_profile_service.dart';
import 'package:smart_reptile/state/species_profile_provider.dart';

void main() {
  group('SpeciesProfile Models', () {
    test('parses SpeciesProfileView and bands correctly from json', () {
      final json = {
        'id': 'b1c855a9-d3e7-4b71-b0f3-8b7a60d00a11',
        'name': 'Rồng Úc (Bearded Dragon)',
        'scientificName': 'Pogona vitticeps',
        'climateZone': 'Arid',
        'isBuiltIn': true,
        'photoperiodHours': 12.0,
        'lightsOnLocalTime': '07:00',
        'lightThresholdLux': 1000.0,
        'minLightHoursPerDay': 8.0,
        'notes': 'Loài bò sát sa mạc cần nhiệt độ sưởi cao.',
        'bands': [
          {
            'metric': 'tempC',
            'unit': '°C',
            'phase': 'day',
            'targetMin': 35.0,
            'targetMax': 40.0,
            'criticalMin': 30.0,
            'criticalMax': 45.0,
            'dwellWarnMinutes': 5,
            'dwellCritMinutes': 2,
            'recoveryMargin': 0.5,
            'enabled': true,
            'sourceRef': 'Reptile Care Manual 2024',
          },
        ],
        'imageUrl': '/api/v1/species-profiles/b1c855a9/image',
        'imageUpdatedAt': '2026-10-18T10:00:00Z',
      };

      final profile = SpeciesProfileView.fromJson(json);

      expect(profile.id, 'b1c855a9-d3e7-4b71-b0f3-8b7a60d00a11');
      expect(profile.name, 'Rồng Úc (Bearded Dragon)');
      expect(profile.scientificName, 'Pogona vitticeps');
      expect(profile.climateZone, ClimateZones.arid);
      expect(profile.isBuiltIn, isTrue);
      expect(profile.bands.length, 1);

      final band = profile.bands.first;
      expect(band.metric, 'tempC');
      expect(band.unit, '°C');
      expect(band.phase, 'day');
      expect(band.targetMin, 35.0);
      expect(band.targetMax, 40.0);
    });
  });

  group('SpeciesProfileProvider State & API', () {
    test(
      'fetchProfiles loads items, distinguishes builtIn vs custom, and filters',
      () async {
        final mockData = {
          'items': [
            {
              'id': '11111111-1111-1111-1111-111111111111',
              'name': 'Rồng Úc',
              'scientificName': 'Pogona vitticeps',
              'climateZone': 'Arid',
              'isBuiltIn': true,
              'photoperiodHours': 12.0,
              'lightsOnLocalTime': '07:00',
              'lightThresholdLux': 1000.0,
              'minLightHoursPerDay': 8.0,
              'bands': [],
            },
            {
              'id': '22222222-2222-2222-2222-222222222222',
              'name': 'Trăn Cây Xanh Tùy Chỉnh',
              'scientificName': 'Morelia viridis',
              'climateZone': 'Tropical',
              'isBuiltIn': false,
              'photoperiodHours': 12.0,
              'lightsOnLocalTime': '06:00',
              'lightThresholdLux': 500.0,
              'minLightHoursPerDay': 6.0,
              'bands': [],
            },
          ],
        };

        final client = MockClient((request) async {
          if (request.url.path == '/api/v1/species-profiles' &&
              request.method == 'GET') {
            return http.Response(
              jsonEncode(mockData),
              200,
              headers: {'content-type': 'application/json'},
            );
          }
          return http.Response('Not found', 404);
        });

        final api = ApiClient(baseUrl: 'http://test', httpClient: client);
        final service = SpeciesProfileService(api: api);
        final provider = SpeciesProfileProvider(service: service);

        expect(provider.isLoading, isFalse);
        expect(provider.profiles, isEmpty);

        await provider.fetchProfiles();

        expect(provider.profiles.length, 2);
        expect(provider.builtInProfiles.length, 1);
        expect(provider.builtInProfiles.first.name, 'Rồng Úc');
        expect(provider.customProfiles.length, 1);
        expect(provider.customProfiles.first.name, 'Trăn Cây Xanh Tùy Chỉnh');

        // Test Search query
        provider.setSearchQuery('Trăn');
        expect(provider.filteredProfiles.length, 1);
        expect(
          provider.filteredProfiles.first.scientificName,
          'Morelia viridis',
        );

        // Test ClimateZone filter
        provider.clearFilters();
        provider.setClimateZoneFilter('Arid');
        expect(provider.filteredProfiles.length, 1);
        expect(provider.filteredProfiles.first.name, 'Rồng Úc');
      },
    );

    test('createProfile and duplicateProfile add to state correctly', () async {
      final client = MockClient((request) async {
        if (request.url.path == '/api/v1/species-profiles' &&
            request.method == 'POST') {
          final body = jsonDecode(request.body) as Map<String, dynamic>;
          return http.Response(
            jsonEncode({
              'id': 'new-uuid',
              'name': body['name'],
              'scientificName': body['scientificName'] ?? '',
              'climateZone': body['climateZone'],
              'isBuiltIn': false,
              'photoperiodHours': body['photoperiodHours'] ?? 12.0,
              'lightsOnLocalTime': body['lightsOnLocalTime'] ?? '07:00',
              'lightThresholdLux': body['lightThresholdLux'] ?? 1000.0,
              'minLightHoursPerDay': body['minLightHoursPerDay'] ?? 8.0,
              'bands': [],
            }),
            200,
            headers: {'content-type': 'application/json'},
          );
        }
        return http.Response('Not found', 404);
      });

      final api = ApiClient(baseUrl: 'http://test', httpClient: client);
      final service = SpeciesProfileService(api: api);
      final provider = SpeciesProfileProvider(service: service);

      final created = await provider.createProfile(
        const CreateSpeciesProfileRequest(
          name: 'Tắc kè hoa Yemen',
          scientificName: 'Chamaeleo calyptratus',
          climateZone: 'SemiArid',
        ),
      );

      expect(created, isNotNull);
      expect(created!.name, 'Tắc kè hoa Yemen');
      expect(provider.profiles.length, 1);
      expect(provider.profiles.first.id, 'new-uuid');
    });

    test(
      'handles backend business errors with friendly Vietnamese messages',
      () async {
        final client = MockClient((request) async {
          if (request.url.path.contains('/delete-immutable')) {
            return http.Response(
              jsonEncode({
                'code': 'builtin_immutable',
                'title': 'Built-in profile is immutable',
              }),
              403,
              headers: {'content-type': 'application/problem+json'},
            );
          }
          if (request.url.path.contains('/delete-in-use')) {
            return http.Response(
              jsonEncode({
                'code': 'profile_in_use',
                'title': 'Profile is assigned to terrarium',
              }),
              409,
              headers: {'content-type': 'application/problem+json'},
            );
          }
          return http.Response('Ok', 200);
        });

        final api = ApiClient(baseUrl: 'http://test', httpClient: client);
        final service = SpeciesProfileService(api: api);
        final provider = SpeciesProfileProvider(service: service);

        // Delete immutable
        final success1 = await provider.deleteProfile('delete-immutable');
        expect(success1, isFalse);
        expect(provider.failure?.code, 'builtin_immutable');
        expect(
          provider.errorMessage,
          contains('Hồ sơ loài mặc định của hệ thống không thể chỉnh sửa'),
        );

        // Delete in-use
        final success2 = await provider.deleteProfile('delete-in-use');
        expect(success2, isFalse);
        expect(provider.failure?.code, 'profile_in_use');
        expect(
          provider.errorMessage,
          contains('Không thể xóa hồ sơ loài vì hiện đang có bể nuôi'),
        );
      },
    );
  });
}
