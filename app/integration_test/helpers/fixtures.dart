/// Canned API payloads for the `hermetic` suite.
///
/// Kept in one place so the widget test and the E2E suite cannot drift into two different ideas of what the API
/// answers: `app/test/widget/login_screen_test.dart` uses the same shape.
library;

/// A signed-in keeper with the two permissions the shell needs to show an enclosure list.
///
/// `permissions` — not `role` — is what the shell gates on (`AccountProfile.hasPermission`), and the web client
/// reads the same payload, so this fixture is the app's and the browser's definition of "an ordinary keeper".
Map<String, dynamic> get keeperProfile => {
  'id': '11111111-1111-4111-8111-111111111111',
  'username': 'keeper',
  'email': 'keeper@vivariumguard.vn',
  'role': 'Owner',
  'roleId': 'a0e00000-0000-4000-8000-000000000001',
  'permissions': ['terrarium.read', 'terrarium.write'],
  'preferredLanguage': 'vi',
  'timeZoneId': 'Asia/Ho_Chi_Minh',
};

/// The payload `POST /api/v1/auth/login` answers with.
///
/// The expiry instants are far in the future on purpose: a fixture that expires during a suite turns a passing case
/// into a token rotation, which is a different test (`E2E-AI-08`).
Map<String, dynamic> loginPayload({Map<String, dynamic>? user}) => {
  'user': user ?? keeperProfile,
  'accessToken': 'e2e.access.token',
  'refreshToken': 'e2e.refresh.token',
  'accessTokenExpiresAtUtc': '2030-01-01T00:00:00Z',
  'refreshTokenExpiresAtUtc': '2030-02-01T00:00:00Z',
};

/// The payload `POST /api/v1/auth/register` answers with — a recovery code shown exactly once.
Map<String, dynamic> registerPayload({
  String recoveryCode = 'E2E0-0000-0000-0001',
}) => {'recoveryCode': recoveryCode, 'status': 'PendingVerification'};

/// The refusal `POST /api/v1/auth/login` answers with for a wrong password.
///
/// RFC 7807 with a stable `code`: the screen renders the sentence from the code, never from the message
/// (`web/src/api/client.ts` states the same rule for the web client).
Map<String, dynamic> invalidCredentialsBody() => {
  'type': 'https://vivariumguard.vn/problems/invalid_credentials',
  'title': 'Invalid credentials',
  'status': 401,
  'code': 'invalid_credentials',
  'detail': 'The username or password is incorrect.',
};

/// Every read endpoint the shell fans out to on sign-in (`AppDataProvider.loadAll` plus the species service).
///
/// A list, not a map, so a suite that starts stubbing before `loadAll` runs cannot silently miss one and leave a
/// real request to the network.
const List<String> shellReadPaths = [
  '/api/v1/terrariums',
  '/api/v1/devices',
  '/api/v1/alerts',
  '/api/v1/species-profiles',
  '/api/v1/time-zones',
];
