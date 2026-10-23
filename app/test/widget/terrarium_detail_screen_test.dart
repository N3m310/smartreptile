import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:provider/provider.dart';
import 'package:smart_reptile/data/api_client.dart';
import 'package:smart_reptile/l10n/generated/app_localizations.dart';
import 'package:smart_reptile/screens/terrarium_detail_screen.dart';
import 'package:smart_reptile/state/terrarium_provider.dart';
import 'package:smart_reptile/theme/app_theme.dart';

void main() {
  testWidgets('Edit terrarium flow updates name and does not throw assertion error', (tester) async {
    final client = MockClient((request) async {
      if (request.method == 'GET' && request.url.path == '/api/v1/terrariums/t-01') {
        return http.Response(
          jsonEncode({
            'id': 't-01',
            'name': 'chuong test',
            'speciesProfileId': 'sp-01',
            'speciesName': 'Arid (desert)',
            'location': 'HoChiMinh',
            'description': 'Test desc',
            'timeZoneId': 'Asia/Ho_Chi_Minh',
            'rowVersion': 'row-v1',
            'device': null,
            'latestSampleAt': null,
            'openAlertCount': 0,
          }),
          200,
          headers: {'content-type': 'application/json'},
        );
      }
      if (request.method == 'PATCH' && request.url.path == '/api/v1/terrariums/t-01') {
        final body = jsonDecode(request.body) as Map<String, dynamic>;
        return http.Response(
          jsonEncode({
            'id': 't-01',
            'name': body['name'],
            'speciesProfileId': 'sp-01',
            'speciesName': 'Arid (desert)',
            'location': body['location'],
            'description': body['description'],
            'timeZoneId': 'Asia/Ho_Chi_Minh',
            'rowVersion': 'row-v2',
            'device': null,
            'latestSampleAt': null,
            'openAlertCount': 0,
          }),
          200,
          headers: {'content-type': 'application/json'},
        );
      }
      if (request.url.path.contains('/telemetry')) {
        return http.Response(
          jsonEncode({'items': [], 'series': [], 'buckets': []}),
          200,
          headers: {'content-type': 'application/json'},
        );
      }
      return http.Response('{}', 200, headers: {'content-type': 'application/json'});
    });

    final api = ApiClient(baseUrl: 'http://localhost', httpClient: client);
    final terrariumProvider = TerrariumProvider(api);

    await tester.pumpWidget(
      MultiProvider(
        providers: [
          ChangeNotifierProvider<TerrariumProvider>.value(value: terrariumProvider),
        ],
        child: MaterialApp(
          locale: const Locale('vi'),
          theme: AppTheme.darkTheme,
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: const TerrariumDetailScreen(terrariumId: 't-01'),
        ),
      ),
    );

    await tester.pumpAndSettle();

    expect(find.text('chuong test'), findsOneWidget);

    // Open PopupMenuButton
    final popupMenu = find.byType(PopupMenuButton<String>);
    expect(popupMenu, findsOneWidget);
    await tester.tap(popupMenu);
    await tester.pumpAndSettle();

    // Tap Edit
    final editItem = find.text('Sửa bể nuôi');
    expect(editItem, findsOneWidget);
    await tester.tap(editItem);
    await tester.pumpAndSettle();

    // Dialog should be open
    expect(find.byType(AlertDialog), findsOneWidget);

    // Change terrarium name
    final nameField = find.widgetWithText(TextFormField, 'Tên bể nuôi');
    expect(nameField, findsOneWidget);
    await tester.enterText(nameField, 'chuong test updated');
    await tester.pumpAndSettle();

    // Tap Save
    final saveButton = find.text('Lưu');
    expect(saveButton, findsOneWidget);
    await tester.tap(saveButton);
    await tester.pumpAndSettle();

    // Verify dialog is closed and updated name is displayed
    expect(find.byType(AlertDialog), findsNothing);
    expect(find.text('chuong test updated'), findsOneWidget);
    expect(find.byType(SnackBar), findsOneWidget);
  });
}
