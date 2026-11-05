import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:smart_reptile/data/api_client.dart';
import 'package:smart_reptile/screens/camera_screen.dart';
import 'package:smart_reptile/state/app_data_provider.dart';

Widget _hosted(Widget child) => MultiProvider(
  providers: [
    Provider<ApiClient>.value(value: ApiClient(baseUrl: 'http://10.0.2.2:8080')),
    ChangeNotifierProvider<AppDataProvider>.value(value: AppDataProvider()),
  ],
  child: MaterialApp(home: child),
);

void main() {
  setUp(() => SharedPreferences.setMockInitialValues(<String, Object>{}));

  testWidgets('opens on the WebRTC mode and says what is missing', (tester) async {
    await tester.pumpWidget(_hosted(const CameraScreen()));
    await tester.pumpAndSettle();

    expect(find.text('WebRTC'), findsOneWidget);
    expect(find.text('URL (MJPEG)'), findsOneWidget);
    // The host has no session token, so the WebRTC panel explains itself instead of showing an empty frame.
    expect(find.textContaining('Cần đăng nhập'), findsOneWidget);
  });

  testWidgets('offers the URL form after switching to MJPEG', (tester) async {
    await tester.pumpWidget(_hosted(const CameraScreen()));
    await tester.pumpAndSettle();

    await tester.tap(find.text('URL (MJPEG)'));
    await tester.pumpAndSettle();

    expect(find.byType(TextField), findsOneWidget);
    expect(find.text('Bắt đầu xem'), findsOneWidget);
  });

  testWidgets('refuses an address that is not http(s)', (tester) async {
    // The form sits below the video frame and the help card; a phone-sized viewport puts the button off-screen.
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(_hosted(const CameraScreen()));
    await tester.pumpAndSettle();

    await tester.tap(find.text('URL (MJPEG)'));
    await tester.pumpAndSettle();

    await tester.enterText(find.byType(TextField), 'rtsp://192.168.1.20/live');
    await tester.tap(find.text('Bắt đầu xem'), warnIfMissed: false);
    await tester.pumpAndSettle();

    expect(find.textContaining('Nhập URL http(s)'), findsOneWidget);
  });
}
