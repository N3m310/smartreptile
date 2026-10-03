import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'core/env.dart';
import 'l10n/generated/app_localizations.dart';
import 'screens/dashboard_screen.dart';
import 'screens/terrariums_screen.dart';
import 'screens/alerts_screen.dart';
import 'screens/history_screen.dart';
import 'screens/settings_screen.dart';
import 'state/app_data_provider.dart';
import 'state/settings_provider.dart';
import 'theme/app_theme.dart';

/// Root widget: theme, localisation and the navigation shell.
class SmartReptileApp extends StatelessWidget {
  const SmartReptileApp({super.key});

  @override
  Widget build(BuildContext context) {
    final settings = context.watch<SettingsProvider>();

    return MaterialApp(
      title: 'TERRAGUARD',
      debugShowCheckedModeBanner: false,
      locale: settings.locale,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      theme: AppTheme.darkTheme,
      darkTheme: AppTheme.darkTheme,
      themeMode: ThemeMode.dark,
      home: const _RootShell(),
    );
  }
}

class _RootShell extends StatefulWidget {
  const _RootShell();

  @override
  State<_RootShell> createState() => _RootShellState();
}

class _RootShellState extends State<_RootShell> {
  int _index = 0;

  final List<Widget> _screens = const [
    DashboardScreen(),
    TerrariumsScreen(),
    AlertsScreen(),
    HistoryScreen(),
    SettingsScreen(),
  ];

  @override
  Widget build(BuildContext context) {
    final appData = context.watch<AppDataProvider>();
    final pendingCount = appData.pendingAlertsCount;

    return Scaffold(
      appBar: AppBar(
        title: Row(
          children: [
            Container(
              width: 32,
              height: 32,
              decoration: BoxDecoration(
                color: AppColors.primary,
                borderRadius: BorderRadius.circular(10),
              ),
              child: const Icon(Icons.shield_outlined,
                  size: 20, color: Colors.white),
            ),
            const SizedBox(width: 10),
            RichText(
              text: const TextSpan(
                style: TextStyle(
                  fontSize: 18,
                  fontWeight: FontWeight.w900,
                  letterSpacing: 0.8,
                  color: AppColors.textMain,
                ),
                children: [
                  TextSpan(text: 'TERRA'),
                  TextSpan(
                    text: 'GUARD',
                    style: TextStyle(color: AppColors.primary),
                  ),
                ],
              ),
            ),
          ],
        ),
        actions: [
          Container(
            margin: const EdgeInsets.only(right: 12),
            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
            decoration: BoxDecoration(
              color: AppColors.bgMain,
              borderRadius: BorderRadius.circular(20),
              border: Border.all(color: AppColors.border),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Container(
                  width: 7,
                  height: 7,
                  decoration: const BoxDecoration(
                    shape: BoxShape.circle,
                    color: AppColors.primary,
                  ),
                ),
                const SizedBox(width: 5),
                const Text(
                  'v${Env.appVersion}',
                  style: TextStyle(
                    fontSize: 10,
                    fontWeight: FontWeight.bold,
                    color: AppColors.textMuted,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
      body: _screens[_index],
      bottomNavigationBar: NavigationBar(
        selectedIndex: _index,
        onDestinationSelected: (value) => setState(() => _index = value),
        destinations: [
          const NavigationDestination(
            icon: Icon(Icons.dashboard_outlined),
            selectedIcon: Icon(Icons.dashboard),
            label: 'Tổng quan',
          ),
          const NavigationDestination(
            icon: Icon(Icons.pets_outlined),
            selectedIcon: Icon(Icons.pets),
            label: 'Terrarium',
          ),
          NavigationDestination(
            icon: pendingCount > 0
                ? Badge(
                    label: Text(
                      '$pendingCount',
                      style: const TextStyle(fontWeight: FontWeight.bold),
                    ),
                    backgroundColor: AppColors.statusDanger,
                    child: const Icon(Icons.warning_amber_rounded),
                  )
                : const Icon(Icons.warning_amber_rounded),
            selectedIcon: pendingCount > 0
                ? Badge(
                    label: Text(
                      '$pendingCount',
                      style: const TextStyle(fontWeight: FontWeight.bold),
                    ),
                    backgroundColor: AppColors.statusDanger,
                    child: const Icon(Icons.warning_rounded),
                  )
                : const Icon(Icons.warning_rounded),
            label: 'Cảnh báo',
          ),
          const NavigationDestination(
            icon: Icon(Icons.show_chart_outlined),
            selectedIcon: Icon(Icons.show_chart),
            label: 'Lịch sử',
          ),
          const NavigationDestination(
            icon: Icon(Icons.settings_outlined),
            selectedIcon: Icon(Icons.settings),
            label: 'Cài đặt',
          ),
        ],
      ),
    );
  }
}

