import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'core/env.dart';
import 'core/permissions.dart';
import 'l10n/generated/app_localizations.dart';
import 'screens/admin/admin_hub_screen.dart';
import 'screens/alerts_screen.dart';
import 'screens/dashboard_screen.dart';
import 'screens/history_screen.dart';
import 'screens/login_screen.dart';
import 'screens/settings_screen.dart';
import 'screens/species_screen.dart';
import 'screens/terrariums_screen.dart';
import 'state/app_data_provider.dart';
import 'state/session_provider.dart';
import 'state/settings_provider.dart';
import 'theme/app_theme.dart';
import 'widgets/mock_data_notice.dart';

/// The permissions that make the administration tab worth showing: either one opens at least one screen behind it.
const List<String> _administrativePermissions = [
  Permissions.dataReadAny,
  Permissions.userRead,
];

/// One destination of the shell: the tab, the screen, and whether that screen is still on the mock models.
class _ShellTab {
  const _ShellTab({
    required this.destination,
    required this.screen,
    this.isMock = true,
  });

  final NavigationDestination destination;
  final Widget screen;

  /// False for a screen that reads the API, which is what decides whether the simulated-data notice is shown over it.
  final bool isMock;
}

/// Root widget: theme, localisation and the navigation shell.
class SmartReptileApp extends StatelessWidget {
  const SmartReptileApp({super.key});

  @override
  Widget build(BuildContext context) {
    final settings = context.watch<SettingsProvider>();
    final session = context.watch<SessionProvider>();

    return MaterialApp(
      title: 'VIVARIUMGUARD',
      debugShowCheckedModeBanner: false,
      locale: settings.locale,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      theme: AppTheme.darkTheme,
      darkTheme: AppTheme.darkTheme,
      themeMode: ThemeMode.dark,
      home: session.isSignedIn || session.isGuestMode
          ? const _RootShell()
          : const LoginScreen(),
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

  @override
  Widget build(BuildContext context) {
    final appData = context.watch<AppDataProvider>();
    final session = context.watch<SessionProvider>();
    final l10n = AppLocalizations.of(context);
    final pendingCount = appData.pendingAlertsCount;

    // The administration tab exists only while the session holds a permission that can open something behind it:
    // `data.read.any` for the get-all views, `user.read` for accounts and roles. A tab that led to a locked screen
    // would be a dead end, and the permission codes — never a role name — are what decides (FR-18).
    final tabs = <_ShellTab>[
      const _ShellTab(
        destination: NavigationDestination(
          icon: Icon(Icons.dashboard_outlined),
          selectedIcon: Icon(Icons.dashboard),
          label: 'Tổng quan',
        ),
        screen: DashboardScreen(),
      ),
      const _ShellTab(
        destination: NavigationDestination(
          icon: Icon(Icons.grass_outlined),
          selectedIcon: Icon(Icons.grass),
          label: 'Terrarium',
        ),
        screen: TerrariumsScreen(),
      ),
      const _ShellTab(
        destination: NavigationDestination(
          icon: Icon(Icons.pets_outlined),
          selectedIcon: Icon(Icons.pets),
          label: 'Hồ sơ loài',
        ),
        screen: SpeciesScreen(),
        isMock: false,
      ),
      _ShellTab(
        destination: NavigationDestination(
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
        screen: const AlertsScreen(),
      ),
      const _ShellTab(
        destination: NavigationDestination(
          icon: Icon(Icons.show_chart_outlined),
          selectedIcon: Icon(Icons.show_chart),
          label: 'Lịch sử',
        ),
        screen: HistoryScreen(),
      ),
      const _ShellTab(
        destination: NavigationDestination(
          icon: Icon(Icons.settings_outlined),
          selectedIcon: Icon(Icons.settings),
          label: 'Cài đặt',
        ),
        screen: SettingsScreen(),
      ),
      if (session.hasAnyPermission(_administrativePermissions))
        _ShellTab(
          destination: NavigationDestination(
            icon: const Icon(Icons.admin_panel_settings_outlined),
            selectedIcon: const Icon(Icons.admin_panel_settings),
            label: l10n.navAdmin,
          ),
          screen: const AdminHubScreen(),
          isMock: false,
        ),
    ];

    // A sign-out while the administration tab is open removes it, so the index has to be clamped rather than
    // trusted: the alternative is a range error on the next frame.
    final index = _index.clamp(0, tabs.length - 1);

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
              child: const Icon(
                Icons.shield_outlined,
                size: 20,
                color: Colors.white,
              ),
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
                  TextSpan(text: 'VIVARIUM'),
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
      // The notice sits above the tabs rather than on each screen: the keeper's tabs are still rendering the M1 mock
      // models, so the honest statement is about them, and it will move into the screens one at a time as they are
      // wired — a screen that shows it is a screen that is still inventing its numbers (task 4.14's rule). The
      // administration tab is *not* on mock models: everything behind it is the real API, so it does not carry the
      // notice, and hiding it there is the same rule read the other way round.
      body: Column(
        children: [
          if (tabs[index].isMock && !context.watch<AppDataProvider>().isLive)
            const MockDataNotice(),
          Expanded(child: tabs[index].screen),
        ],
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: index,
        onDestinationSelected: (value) => setState(() => _index = value),
        destinations: [for (final tab in tabs) tab.destination],
      ),
    );
  }
}
