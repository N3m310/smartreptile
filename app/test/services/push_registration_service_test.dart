import 'dart:async';
import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:smart_reptile/data/api_client.dart';
import 'package:smart_reptile/services/push_registration_service.dart';

void main() {
  test('registers an authorized FCM token for the signed-in account', () async {
    final tokenChanges = StreamController<String>();
    addTearDown(tokenChanges.close);

    http.Request? capturedRequest;
    final api = ApiClient(
      baseUrl: 'http://localhost',
      httpClient: MockClient((request) async {
        capturedRequest = request;
        return http.Response('{}', 200);
      }),
    )..accessToken = 'access-token';

    final registration = PushRegistrationService(
      api,
      () async => true,
      () async => 'device-token',
      tokenChanges.stream,
    );

    await registration.signedIn();

    expect(capturedRequest?.url.path, '/api/v1/auth/me');
    expect(capturedRequest?.headers['authorization'], 'Bearer access-token');
    expect(jsonDecode(capturedRequest!.body), {
      'channelFcmEnabled': true,
      'fcmToken': 'device-token',
    });
  });

  test('denied permission disables FCM and clears the stored token', () async {
    final tokenChanges = StreamController<String>();
    addTearDown(tokenChanges.close);

    http.Request? capturedRequest;
    final api = ApiClient(
      baseUrl: 'http://localhost',
      httpClient: MockClient((request) async {
        capturedRequest = request;
        return http.Response('{}', 200);
      }),
    );

    final registration = PushRegistrationService(
      api,
      () async => false,
      () async => fail('token must not be requested'),
      tokenChanges.stream,
    );

    await registration.signedIn();

    expect(jsonDecode(capturedRequest!.body), {
      'channelFcmEnabled': false,
      'fcmToken': '',
    });
  });

  test('token refreshes update the account only while signed in', () async {
    final tokenChanges = StreamController<String>();
    addTearDown(tokenChanges.close);

    final requests = <http.Request>[];
    final api = ApiClient(
      baseUrl: 'http://localhost',
      httpClient: MockClient((request) async {
        requests.add(request);
        return http.Response('{}', 200);
      }),
    );

    final registration = PushRegistrationService(
      api,
      () async => true,
      () async => 'initial-token',
      tokenChanges.stream,
    );

    await registration.signedIn();
    tokenChanges.add('rotated-token');
    await Future<void>.delayed(Duration.zero);
    expect(requests, hasLength(2));
    expect(jsonDecode(requests.last.body)['fcmToken'], 'rotated-token');

    await registration.signedOut();
    expect(jsonDecode(requests.last.body), {
      'channelFcmEnabled': false,
      'fcmToken': '',
    });
    final requestCountAfterSignOut = requests.length;

    tokenChanges.add('late-token');
    await Future<void>.delayed(Duration.zero);
    expect(requests, hasLength(requestCountAfterSignOut));
  });
}
