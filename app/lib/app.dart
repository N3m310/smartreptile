import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'core/env.dart';
import 'core/permissions.dart';
import 'l10n/generated/app_localizations.dart';
import 'screens/admin/admin_hub_screen.dart';
import 'screens/dashboard_screen.dart';
import 'screens/terrariums_screen.dart';
import 'screens/alerts_screen.dart';
import 'screens/devices_screen.dart';
import 'screens/history_screen.dart';
import 'screens/login_screen.dart';
import 'screens/profile_screen.dart';
import 'screens/species_screen.dart';
import 'state/app_data_provider.dart';
import 'state/session_provider.dart';
import 'state/settings_provider.dart';
import 'state/terrarium_provider.dart';
import 'theme/app_theme.dart';

/// The permissions that make the administration tab worth showing: either one opens at least one screen behind it.
const List<String> _administrativePermissions = [
  Permissions.dataReadAny,
  Permissions.userRead,
];

/// One destination of the shell: the tab and the screen behind it.
class _ShellTab {
  const _ShellTab({required this.destination, required this.screen});

  final Widget destination;
  final Widget screen;
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
      // Vietnamese unless the keeper picked another language. Deliberately not "follow the device": every screen in
      // this shell is written in Vietnamese, so an `en-US` handset only mixed English catalogue strings into
      // Vietnamese copy — an English dialog title over a Vietnamese alert, which is what this used to produce. The
      // prototype keeps the provider's own "follow the device" default, because its screens are English by design.
      locale: settings.locale ?? const Locale('vi'),
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

class _RootShellState extends State<_RootShell> with WidgetsBindingObserver {
  int _index = 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  /// Re-reads the deployment's data when the app comes back to the foreground.
  ///
  /// Ownership is shared: an administrator can give an enclosure to another account, a keeper can claim a board, an
  /// alert can be acknowledged from the web. None of that reaches a screen that is already built, so a resumed app
  /// asks again instead of showing what it left behind.
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state != AppLifecycleState.resumed) return;
    if (!mounted || !context.read<SessionProvider>().isSignedIn) return;
    context.read<AppDataProvider>().loadAll();
    context.read<TerrariumProvider>().loadTerrariums();
  }

  @override
  Widget build(BuildContext context) {
    final appData = context.watch<AppDataProvider>();
    final session = context.watch<SessionProvider>();
    final l10n = AppLocalizations.of(context);
    final pendingCount = appData.pendingAlertsCount;
    final isAdmin =
        (session.user?.role.toLowerCase() == 'administrator') ||
        (session.user?.role.toLowerCase() == 'admin') ||
        session.hasAnyPermission(_administrativePermissions);
    final isOwner = session.user?.role.toLowerCase() == 'owner';

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
          icon: Icon(Icons.pets_outlined),
          selectedIcon: Icon(Icons.pets),
          label: 'Terrarium',
        ),
        screen: TerrariumsScreen(),
      ),
      const _ShellTab(
        destination: NavigationDestination(
          icon: Icon(Icons.biotech_outlined),
          selectedIcon: Icon(Icons.biotech),
          label: 'Hồ sơ loài',
        ),
        screen: SpeciesScreen(),
      ),
      if (!isOwner)
        _ShellTab(
          destination: isAdmin
              ? const NavigationDestination(
                  icon: Icon(Icons.developer_board_outlined),
                  selectedIcon: Icon(Icons.developer_board),
                  label: 'Thiết bị',
                )
              : const NavigationDestination(
                  icon: Icon(Icons.developer_board_outlined),
                  selectedIcon: Icon(Icons.developer_board),
                  label: 'Thiết bị IoT',
                ),
          screen: const DevicesScreen(),
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
      if (session.hasAnyPermission(_administrativePermissions))
        _ShellTab(
          destination: NavigationDestination(
            icon: const Icon(Icons.admin_panel_settings_outlined),
            selectedIcon: const Icon(Icons.admin_panel_settings),
            label: l10n.navAdmin,
          ),
          screen: const AdminHubScreen(),
        )
      else
        _ShellTab(
          destination: NavigationDestination(
            icon: const Icon(Icons.person_outline),
            selectedIcon: const Icon(Icons.person),
            label: l10n.navProfile,
          ),
          screen: const ProfileScreen(),
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
            // Flexible + FittedBox: on a narrow handset the app bar gives the title less width than the wordmark
            // wants, and a bare Row paints the last letters past the edge (the fullstack E2E caught it at 411 dp).
            // Scaling down keeps the whole wordmark readable instead of clipping it.
            Flexible(
              child: FittedBox(
                fit: BoxFit.scaleDown,
                alignment: Alignment.centerLeft,
                child: RichText(
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
      body: tabs[index].screen,
      bottomNavigationBar: DefaultTextStyle.merge(
        textAlign: TextAlign.center,
        child: NavigationBar(
          selectedIndex: index,
          onDestinationSelected: (value) => setState(() => _index = value),
          destinations: [for (final tab in tabs) tab.destination],
        ),
      ),
    );
  }
}
