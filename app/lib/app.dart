import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'core/env.dart';
import 'l10n/generated/app_localizations.dart';
import 'screens/home_screen.dart';
import 'state/settings_provider.dart';

/// Root widget: theme, localisation and the navigation shell (§02-design/04 §2).
///
/// M1 keeps the shell to the four tabs the design specifies, with Home implemented and the others showing an
/// explicit "arrives in M2/M4" state rather than an empty screen that looks broken.
class SmartReptileApp extends StatelessWidget {
  const SmartReptileApp({super.key});

  @override
  Widget build(BuildContext context) {
    final settings = context.watch<SettingsProvider>();

    return MaterialApp(
      title: 'SmartReptile',
      debugShowCheckedModeBanner: false,
      locale: settings.locale,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      theme: _theme(Brightness.light),
      darkTheme: _theme(Brightness.dark),
      themeMode: settings.themeMode,
      home: const _RootShell(),
    );
  }

  ThemeData _theme(Brightness brightness) {
    final scheme = ColorScheme.fromSeed(
      seedColor: const Color(0xFF2E7D32),
      brightness: brightness,
    );

    return ThemeData(
      useMaterial3: true,
      colorScheme: scheme,
      cardTheme: const CardThemeData(elevation: 0),
      visualDensity: VisualDensity.standard,
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
    final l10n = AppLocalizations.of(context);
    final tabs = [
      _Tab(l10n.tabHome, Icons.home_outlined, const HomeScreen()),
      _Tab(
        l10n.tabHistory,
        Icons.show_chart,
        const _ComingSoon(feature: 'History (M4)'),
      ),
      _Tab(
        l10n.tabAlerts,
        Icons.notifications_none,
        const _ComingSoon(feature: 'Alerts (M3)'),
      ),
      _Tab(
        l10n.tabMore,
        Icons.more_horiz,
        const _ComingSoon(feature: 'Terrariums, devices, settings (M2/M4)'),
      ),
    ];

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.appTitle),
        actions: [
          Padding(
            padding: const EdgeInsets.only(right: 12),
            child: Center(
              child: Text(
                'v${Env.appVersion}',
                style: Theme.of(context).textTheme.labelSmall,
              ),
            ),
          ),
        ],
      ),
      body: tabs[_index].screen,
      bottomNavigationBar: NavigationBar(
        selectedIndex: _index,
        onDestinationSelected: (value) => setState(() => _index = value),
        destinations: [
          for (final tab in tabs)
            NavigationDestination(icon: Icon(tab.icon), label: tab.label),
        ],
      ),
    );
  }
}

class _Tab {
  const _Tab(this.label, this.icon, this.screen);

  final String label;
  final IconData icon;
  final Widget screen;
}

class _ComingSoon extends StatelessWidget {
  const _ComingSoon({required this.feature});

  final String feature;

  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(32),
      child: Text(
        '$feature — scaffolded, not implemented yet.\n'
        'See docs/03-implementation/07-implementation-roadmap.md for the milestone plan.',
        textAlign: TextAlign.center,
        style: Theme.of(context).textTheme.bodySmall,
      ),
    ),
  );
}
