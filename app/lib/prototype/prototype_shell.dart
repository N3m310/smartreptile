/// The prototype's root: theme, the tab shell, and the app bar that carries the demo clock.
///
/// Two things here are deliberate and worth stating:
///  * the prototype banner and the demo-clock chip are **always visible**, because the most expensive mistake a
///    prototype can cause is a screenshot of fake data being read as a real measurement;
///  * the More tab is a list rather than a fifth bottom-bar item, matching the design's four-item navigation rule
///    (`02-design/04` §2) while still reaching every screen.
library;

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../core/env.dart';
import '../l10n/generated/app_localizations.dart';
import '../state/settings_provider.dart';
import 'labels.dart';
import 'prototype_state.dart';
import 'screens/alerts_screen.dart';
import 'screens/dashboard_screen.dart';
import 'screens/devices_screen.dart';
import 'screens/history_screen.dart';
import 'screens/login_screen.dart';
import 'screens/report_and_settings.dart';
import 'screens/rule_lab_screen.dart';
import 'screens/thresholds_screen.dart';
import 'widgets/prototype_ui.dart';

/// The prototype application widget.
class PrototypeApp extends StatelessWidget {
  /// Creates the app.
  const PrototypeApp({super.key});

  @override
  Widget build(BuildContext context) {
    final state = context.watch<PrototypeState>();
    // Theme and language stay on the app's own SettingsProvider, which already persists them. A second copy of
    // "is it dark?" is a bug waiting to be reported as "the switch does nothing".
    final settings = context.watch<SettingsProvider>();

    return MaterialApp(
      title: 'SmartReptile (prototype)',
      debugShowCheckedModeBanner: false,
      locale: settings.locale,
      // The delegates are not optional: `MetricCard` and the rest of the shared widgets resolve their labels through
      // `AppLocalizations.of(context)`, whose getter is non-nullable. Without them every card throws and Flutter
      // paints its red error box — with no exception in the terminal, which is how this was found the hard way.
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      theme: _theme(Brightness.light),
      darkTheme: _theme(Brightness.dark),
      themeMode: settings.themeMode,
      home: state.isSignedOut ? const LoginScreen() : const PrototypeShell(),
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
      cardTheme: const CardThemeData(elevation: 0, margin: EdgeInsets.zero),
      visualDensity: VisualDensity.compact,
      sliderTheme: const SliderThemeData(trackHeight: 2),
    );
  }
}

/// The four-tab shell. Every other screen hangs off it.
class PrototypeShell extends StatefulWidget {
  /// Creates the shell.
  const PrototypeShell({super.key});

  @override
  State<PrototypeShell> createState() => _PrototypeShellState();
}

class _PrototypeShellState extends State<PrototypeShell> {
  int _index = 0;

  @override
  Widget build(BuildContext context) {
    final state = context.watch<PrototypeState>();
    final tabs = <Widget>[
      const DashboardScreen(),
      const HistoryScreen(),
      const AlertsScreen(),
      const _MoreScreen(),
    ];

    return Scaffold(
      appBar: AppBar(
        titleSpacing: 12,
        title: Row(
          children: [
            const Icon(Icons.terrain_outlined, size: 20),
            const SizedBox(width: 8),
            const Text(Labels.appTitle),
            const SizedBox(width: 8),
            Text(
              'v${Env.appVersion}',
              style: Theme.of(context).textTheme.labelSmall,
            ),
          ],
        ),
        actions: [
          if (state.terrariums.length > 1)
            Padding(
              padding: const EdgeInsets.only(right: 4),
              child: Center(
                child: DropdownButtonHideUnderline(
                  child: DropdownButton<String>(
                    value: state.activeTerrariumId,
                    style: Theme.of(context).textTheme.bodySmall,
                    items: [
                      for (final terrarium in state.terrariums)
                        DropdownMenuItem(
                          value: terrarium.id,
                          child: Text(terrarium.name),
                        ),
                    ],
                    onChanged: (value) {
                      if (value != null) {
                        state.selectTerrarium(value);
                      }
                    },
                  ),
                ),
              ),
            ),
          Padding(
            padding: const EdgeInsets.only(right: 12),
            child: Center(
              child: ActionChip(
                avatar: const Icon(Icons.schedule, size: 14),
                label: Text(
                  formatTime(state.demoNow),
                  style: const TextStyle(fontSize: 11),
                ),
                onPressed: () =>
                    state.advanceDemoClock(const Duration(minutes: 10)),
                tooltip:
                    '${Labels.demoClockLabel} — ${Labels.demoClockAdvance}',
              ),
            ),
          ),
        ],
      ),
      body: tabs[_index],
      bottomNavigationBar: NavigationBar(
        selectedIndex: _index,
        onDestinationSelected: (value) => setState(() => _index = value),
        destinations: [
          const NavigationDestination(
            icon: Icon(Icons.home_outlined),
            label: Labels.tabHome,
          ),
          const NavigationDestination(
            icon: Icon(Icons.show_chart),
            label: Labels.tabHistory,
          ),
          NavigationDestination(
            icon: Badge(
              isLabelVisible: state.openAlertCount > 0,
              label: Text('${state.openAlertCount}'),
              child: const Icon(Icons.notifications_none),
            ),
            label: Labels.tabAlerts,
          ),
          const NavigationDestination(
            icon: Icon(Icons.more_horiz),
            label: Labels.tabMore,
          ),
        ],
      ),
    );
  }
}

class _MoreScreen extends StatelessWidget {
  const _MoreScreen();

  @override
  Widget build(BuildContext context) {
    final state = context.watch<PrototypeState>();

    void open(Widget screen) =>
        Navigator.of(context)
            .push(MaterialPageRoute<void>(builder: (_) => screen));

    return ListView(
      padding: const EdgeInsets.fromLTRB(12, 12, 12, 32),
      children: [
        Callout(
          title: Labels.prototypeTitle,
          message: Labels.prototypeBody,
          color: ProtoColors.prototype,
          icon: Icons.science_outlined,
          actionLabel: Labels.prototypeOpenLab,
          onAction: () => open(const RuleLabScreen()),
        ),
        const SizedBox(height: 12),
        SectionCard(
          title: Labels.settingsPrototype,
          child: Row(
            children: [
              Expanded(
                child: Text(
                  '${Labels.demoClockLabel}: ${formatInstant(state.demoNow)}\n'
                  '${Labels.demoClockHint}',
                  style: Theme.of(context).textTheme.labelSmall,
                ),
              ),
              OutlinedButton(
                onPressed: () =>
                    state.advanceDemoClock(const Duration(minutes: 10)),
                child: const Text(Labels.demoClockAdvance),
              ),
              const SizedBox(width: 6),
              OutlinedButton(
                onPressed: state.resetDemoClock,
                child: const Text(Labels.demoClockReset),
              ),
            ],
          ),
        ),
        const SizedBox(height: 12),
        SectionCard(
          child: Column(
            children: [
              _MoreItem(
                icon: Icons.terrain_outlined,
                label: Labels.moreTerrariums,
                onTap: () => open(const TerrariumsScreen()),
                trailing: '${state.terrariums.length}',
              ),
              _MoreItem(
                icon: Icons.memory,
                label: Labels.moreDevices,
                onTap: () => open(const DevicesScreen()),
                trailing: '${state.devices.length}',
              ),
              _MoreItem(
                icon: Icons.rule_outlined,
                label: Labels.thresholdsTitle,
                onTap: () => open(
                  ThresholdsScreen(terrariumId: state.activeTerrariumId),
                ),
              ),
              _MoreItem(
                icon: Icons.summarize_outlined,
                label: Labels.moreReport,
                onTap: () => open(const ReportScreen()),
              ),
              _MoreItem(
                icon: Icons.science_outlined,
                label: Labels.moreRuleLab,
                onTap: () => open(const RuleLabScreen()),
                trailing: '12 scenarios',
              ),
              _MoreItem(
                icon: Icons.settings_outlined,
                label: Labels.moreSettings,
                onTap: () => open(const SettingsScreen()),
              ),
              _MoreItem(
                icon: Icons.monitor_heart_outlined,
                label: Labels.moreDiagnostics,
                onTap: () => open(const DiagnosticsScreen()),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _MoreItem extends StatelessWidget {
  const _MoreItem({
    required this.icon,
    required this.label,
    required this.onTap,
    this.trailing,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;
  final String? trailing;

  @override
  Widget build(BuildContext context) => ListTile(
    contentPadding: EdgeInsets.zero,
    dense: true,
    leading: Icon(icon, size: 18),
    title: Text(label, style: const TextStyle(fontSize: 13)),
    trailing: Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        if (trailing != null)
          Text(trailing!, style: Theme.of(context).textTheme.labelSmall),
        const SizedBox(width: 4),
        const Icon(Icons.chevron_right, size: 18),
      ],
    ),
    onTap: onTap,
  );
}
