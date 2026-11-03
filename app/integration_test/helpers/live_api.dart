import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:smart_reptile/core/env.dart';

/// The real backend, driven by `dart:http` from the test.
///
/// The `fullstack` suite creates its own fixture through the **public API** — register, sign in, read the seeded
/// species catalogue, create an enclosure — rather than seeding the database. That way the fixture cannot drift
/// from the contract the app uses, and the run needs no SQL and no seeder flag (`scripts/demo-create.ps1` makes the
/// same argument for its own script).
///
/// Base URL is `Env.apiBase`, i.e. the same `--dart-define=API_BASE` the app was built with, so the test and the
/// app cannot disagree about which server they are talking to.
class LiveApi {
  LiveApi({http.Client? client, String? baseUrl})
    : _http = client ?? http.Client(),
      _base = baseUrl ?? '${Env.apiBase}/api/v1';

  final http.Client _http;
  final String _base;

  /// Registers a throwaway account and signs it in.
  ///
  /// The email domain is `.test`, which cannot receive mail: nothing here is meant to outlive the run.
  Future<LiveSession> registerAndSignIn(
    String username,
    String password,
  ) async {
    final registered = await _post('/auth/register', {
      'username': username,
      'email': '$username@example.test',
      'password': password,
    });
    if (registered.statusCode != 200 &&
        registered.statusCode != 201 &&
        registered.statusCode != 202) {
      throw StateError(
        'register failed: ${registered.statusCode} ${registered.body}',
      );
    }

    final login = await _post('/auth/login', {
      'usernameOrEmail': username,
      'password': password,
    });
    if (login.statusCode != 200) {
      throw StateError('login failed: ${login.statusCode} ${login.body}');
    }
    final payload = jsonDecode(login.body) as Map<String, dynamic>;
    final user = payload['user'] as Map<String, dynamic>;
    return LiveSession(
      accessToken: payload['accessToken'] as String,
      refreshToken: payload['refreshToken'] as String,
      userId: user['id'] as String,
      username: username,
      password: password,
    );
  }

  /// Id of the first seeded species profile — the value `POST /terrariums` requires.
  Future<String> firstSpeciesProfileId(String accessToken) async {
    final response = await _http.get(
      Uri.parse('$_base/species-profiles'),
      headers: _auth(accessToken),
    );
    if (response.statusCode != 200) {
      throw StateError(
        'species profiles: ${response.statusCode} ${response.body}',
      );
    }
    final items =
        (jsonDecode(response.body) as Map<String, dynamic>)['items'] as List;
    if (items.isEmpty) {
      throw StateError('the API has no seeded species profile to attach to');
    }
    return (items.first as Map<String, dynamic>)['id'] as String;
  }

  /// Creates an enclosure and answers its id.
  Future<String> createTerrarium(
    String accessToken, {
    required String name,
    required String speciesProfileId,
  }) async {
    final response = await _post('/terrariums', {
      'name': name,
      'speciesProfileId': speciesProfileId,
      'location': 'E2E bench',
      'description': 'Created by integration_test/e2e_fullstack_test.dart',
    }, accessToken: accessToken);
    if (response.statusCode != 200 && response.statusCode != 201) {
      throw StateError(
        'create terrarium: ${response.statusCode} ${response.body}',
      );
    }
    return (jsonDecode(response.body) as Map<String, dynamic>)['id'] as String;
  }

  /// Names of the enclosures the account can see — the fixture teardown asserts this is empty afterwards.
  Future<List<String>> terrariumNames(String accessToken) async {
    final response = await _http.get(
      Uri.parse('$_base/terrariums'),
      headers: _auth(accessToken),
    );
    if (response.statusCode != 200) {
      throw StateError('terrariums: ${response.statusCode} ${response.body}');
    }
    final items =
        (jsonDecode(response.body) as Map<String, dynamic>)['items'] as List;
    return [
      for (final item in items)
        (item as Map<String, dynamic>)['name'] as String,
    ];
  }

  /// Deletes an enclosure. Answers the status code rather than throwing: teardown runs even when a case failed.
  ///
  /// `allowUnboundDevice` is the API's own answer to "this enclosure has a board" (`409 conflict_device_bound`
  /// without it): the fixture bound a device on purpose, so teardown unbinds and deletes in one step.
  Future<int> deleteTerrarium(
    String accessToken,
    String id, {
    bool allowUnboundDevice = true,
  }) async {
    final uri = Uri.parse(
      '$_base/terrariums/$id?allowUnboundDevice=$allowUnboundDevice',
    );
    final response = await _http.delete(uri, headers: _auth(accessToken));
    return response.statusCode;
  }

  /// Writes the band the alert case judges against, and answers the effective set the API stored.
  ///
  /// A one-minute dwell is the shortest the API accepts (`threshold_dwell_invalid` below 1), and it is what makes
  /// `E2E-AI-14` finish in a test rather than in the five minutes the seeded profile asks for. The band is `phase:
  /// any` so the case does not depend on whether the run happens during the enclosure's photoperiod.
  Future<void> setTemperatureOverride(
    String accessToken,
    String terrariumId, {
    num targetMin = 24,
    num targetMax = 30,
    int dwellWarnMinutes = 1,
    int dwellCritMinutes = 1,
  }) async {
    final response = await _http.put(
      Uri.parse('$_base/terrariums/$terrariumId/thresholds'),
      headers: _auth(accessToken),
      body: jsonEncode({
        'overrides': [
          {
            'metric': 'tempC',
            'phase': 'any',
            'targetMin': targetMin,
            'targetMax': targetMax,
            'dwellWarnMinutes': dwellWarnMinutes,
            'dwellCritMinutes': dwellCritMinutes,
            'recoveryMargin': 0.5,
            'note': 'integration_test/e2e_alerts_test.dart',
          },
        ],
      }),
    );
    if (response.statusCode != 200) {
      throw StateError(
        'threshold override: ${response.statusCode} ${response.body}',
      );
    }
  }

  /// Registers an unclaimed board and answers the code it would show on its screen (anonymous route).
  ///
  /// Both identifiers are validated by the API: a chip id may only contain letters and digits (`chip_id_invalid`
  /// otherwise) and a MAC is required (`mac_required`). The MAC is derived from [chipId] so a run's boards are
  /// distinct without a random source.
  Future<SelfRegisteredDevice> selfRegisterDevice(String chipId) async {
    final digits = chipId.replaceAll(RegExp('[^0-9]'), '').padLeft(12, '0');
    final mac = [for (var i = 0; i < 12; i += 2) digits.substring(i, i + 2)]
        .join(':');
    final response = await _post('/devices/self-register', {
      'chipId': chipId,
      'macAddress': mac,
      'firmwareVersion': '1.0.0',
    });
    if (response.statusCode != 201 && response.statusCode != 409) {
      throw StateError(
        'self-register: ${response.statusCode} ${response.body}',
      );
    }
    final payload = jsonDecode(response.body) as Map<String, dynamic>;
    return SelfRegisteredDevice(
      deviceId: payload['deviceId'] as String,
      claimCode: payload['claimCode'] as String,
    );
  }

  /// Binds the board to an enclosure and answers the secret the API hands out exactly once.
  Future<String> claimDevice(
    String accessToken, {
    required String claimCode,
    required String terrariumId,
    String deviceName = 'E2E board',
  }) async {
    final response = await _post('/devices/claim', {
      'claimCode': claimCode,
      'terrariumId': terrariumId,
      'deviceName': deviceName,
    }, accessToken: accessToken);
    if (response.statusCode != 200) {
      throw StateError('claim: ${response.statusCode} ${response.body}');
    }
    return (jsonDecode(response.body) as Map<String, dynamic>)['secret']
        as String;
  }

  /// Submits a telemetry batch over the HTTPS fallback — the same pipeline MQTT feeds (`IngestEndpoints`).
  ///
  /// The batch is stamped **in the past** and spread over three minutes so the evaluator sees a dwell window that
  /// has already elapsed: a case that published "now" and waited would need the five minutes the seeded profile
  /// asks for. Four samples, all above `TargetMax`, with `dwellWarnMinutes` ≥ 1 on the band.
  Future<int> ingestHotBatch({
    required String deviceId,
    required String secret,
    required int seq,
    double temperatureC = 35.5,
    int sampleCount = 4,
    Duration spread = const Duration(minutes: 3),
  }) async {
    final start = DateTime.now().toUtc().subtract(spread);
    final stepSeconds = (spread.inSeconds / (sampleCount - 1)).round();
    final response = await _http.post(
      Uri.parse('$_base/ingest/http'),
      headers: {
        'Authorization': 'Device $deviceId.$secret',
        'Content-Type': 'application/json',
      },
      body: jsonEncode({
        'deviceId': deviceId,
        'seq': seq,
        'fw': '1.0.0',
        'ts': start.toIso8601String().replaceFirst(RegExp(r'\.\d+'), ''),
        'samples': [
          for (var i = 0; i < sampleCount; i++)
            {
              't': i * stepSeconds,
              'tf': temperatureC + i * 0.1,
              'rh': 50.0,
              'lux': 1000.0,
            },
        ],
      }),
    );
    return response.statusCode;
  }

  /// The alerts the account can see, as `{metric, severity, terrariumId}` maps.
  Future<List<Map<String, dynamic>>> alerts(String accessToken) async {
    final response = await _http.get(
      Uri.parse('$_base/alerts'),
      headers: _auth(accessToken),
    );
    if (response.statusCode != 200) {
      throw StateError('alerts: ${response.statusCode} ${response.body}');
    }
    final items =
        (jsonDecode(response.body) as Map<String, dynamic>)['items'] as List;
    return items.cast<Map<String, dynamic>>();
  }

  Map<String, String> _auth(String token) => {
    'Authorization': 'Bearer $token',
    'Content-Type': 'application/json',
  };

  Future<http.Response> _post(
    String path,
    Map<String, dynamic> body, {
    String? accessToken,
  }) => _http
      .post(
        Uri.parse('$_base$path'),
        headers: accessToken == null
            ? {'Content-Type': 'application/json'}
            : _auth(accessToken),
        body: jsonEncode(body),
      )
      .timeout(const Duration(seconds: 20));

  /// Releases the underlying client.
  void close() => _http.close();
}

/// A board that registered but has not been claimed yet: the id the API assigned and the code it would show.
class SelfRegisteredDevice {
  /// Creates the pair.
  const SelfRegisteredDevice({required this.deviceId, required this.claimCode});

  /// The board's public id (`sr-…`).
  final String deviceId;

  /// The code a keeper types into the claim screen.
  final String claimCode;
}

/// The account a `fullstack` run creates for itself.
class LiveSession {
  /// Creates a session.
  const LiveSession({
    required this.accessToken,
    required this.refreshToken,
    required this.userId,
    required this.username,
    required this.password,
  });

  /// Bearer token for the fixture calls.
  final String accessToken;

  /// Refresh token, unused by the fixture but part of the login contract.
  final String refreshToken;

  /// The account's id.
  final String userId;

  /// Login name, typed into the app.
  final String username;

  /// Password, typed into the app.
  final String password;
}
