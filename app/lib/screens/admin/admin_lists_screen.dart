import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/formatting.dart';
import '../../core/paging.dart';
import '../../core/permissions.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../l10n/labels.dart';
import '../../models/admin_models.dart';
import '../../state/admin_provider.dart';
import '../../theme/app_theme.dart';
import 'admin_widgets.dart';

/// Every account's enclosures, boards and alerts — the get-all view (`data.read.any`).
///
/// Three tabs rather than three screens, because they are one question asked three ways ("what is out there?") and
/// hopping between them is how an administrator correlates a silent board with the alerts it should have raised.
/// Each tab is paged and searchable on its own: the filters mean different things, and a shared search box would
/// have to guess which list it is filtering.
class AdminListsScreen extends StatelessWidget {
  /// Creates the screen.
  const AdminListsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return DefaultTabController(
      length: 4,
      child: Scaffold(
        backgroundColor: AppColors.bgMain,
        appBar: AppBar(
          title: Text(l10n.adminListsTitle),
          bottom: TabBar(
            isScrollable: true,
            tabAlignment: TabAlignment.start,
            tabs: [
              Tab(text: l10n.adminTabTerrariums),
              Tab(text: l10n.adminTabDevices),
              Tab(text: l10n.adminTabAlerts),
              Tab(text: l10n.adminTabSpeciesProfiles),
            ],
          ),
        ),
        body: const AdminGate(
          permission: Permissions.dataReadAny,
          child: TabBarView(
            children: [
              _TerrariumList(),
              _DeviceList(),
              _AlertList(),
              _SpeciesProfileList(),
            ],
          ),
        ),
      ),
    );
  }
}

/// The shared shape of one paged tab: a search box, the rows, and a pager.
class _PagedList<T> extends StatelessWidget {
  const _PagedList({
    required this.state,
    this.searchHint,
    this.onSearch,
    required this.onPrevious,
    required this.onNext,
    required this.onRetry,
    required this.rowBuilder,
    this.filters,
  });

  final PagedState<T>? state;
  final String? searchHint;
  final ValueChanged<String>? onSearch;
  final VoidCallback onPrevious;
  final VoidCallback onNext;
  final VoidCallback onRetry;
  final Widget Function(BuildContext context, T row) rowBuilder;
  final Widget? filters;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final page = state;

    if (page == null) {
      return const AdminLoading();
    }

    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        // The alerts list has no search of its own on the API: it filters by family and state instead, so an empty
        // box that sent nothing would be worse than no box.
        if (searchHint != null)
          AdminSearchField(hint: searchHint!, onSubmitted: onSearch!),
        if (filters != null) ...[
          if (searchHint != null) const SizedBox(height: 10),
          filters!,
        ],
        const SizedBox(height: 12),
        if (page.failure != null)
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: AdminErrorView(failure: page.failure!, onRetry: onRetry),
          ),
        if (page.isLoading) const AdminLoading(),
        if (!page.isLoading && page.items.isEmpty && page.failure == null)
          AdminEmptyView(message: l10n.adminNoRows),
        for (final row in page.items) rowBuilder(context, row),
        if (page.items.isNotEmpty)
          AdminPager(state: page, onPrevious: onPrevious, onNext: onNext),
      ],
    );
  }
}

/// A card standing in for one row of a table, so no list needs a horizontal scroll gesture to be read.
class _RowCard extends StatelessWidget {
  const _RowCard({
    required this.title,
    required this.subtitle,
    required this.badges,
    required this.facts,
  });

  final String title;
  final String? subtitle;
  final List<Widget> badges;
  final List<Widget> facts;

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: AppColors.bgCard,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  title,
                  style: const TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.bold,
                    color: AppColors.textMain,
                  ),
                ),
              ),
              for (final badge in badges) ...[
                const SizedBox(width: 6),
                badge,
              ],
            ],
          ),
          if (subtitle != null) ...[
            const SizedBox(height: 2),
            Text(
              subtitle!,
              style: const TextStyle(fontSize: 11, color: AppColors.textMuted),
            ),
          ],
          const SizedBox(height: 8),
          ...facts,
        ],
      ),
    );
  }
}

/// Every account's enclosures, with the board in each and its owner.
class _TerrariumList extends StatefulWidget {
  const _TerrariumList();

  @override
  State<_TerrariumList> createState() => _TerrariumListState();
}

class _TerrariumListState extends State<_TerrariumList> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        context.read<AdminProvider>().loadTerrariums();
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final admin = context.watch<AdminProvider>();
    final page = admin.terrariums;

    return _PagedList<AdminTerrarium>(
      state: page,
      searchHint: l10n.adminSearchTerrariums,
      onSearch: (value) => admin.loadTerrariums(search: value),
      onPrevious: () =>
          admin.loadTerrariums(skip: page!.skip - adminPageSize),
      onNext: () => admin.loadTerrariums(skip: page!.skip + adminPageSize),
      onRetry: () => admin.loadTerrariums(skip: page?.skip ?? 0),
      rowBuilder: (context, row) => _RowCard(
        title: row.name,
        subtitle: row.speciesProfileName,
        badges: [
          AdminStatusPill(
            label: terrariumStatusLabel(l10n, row.status),
            color: row.status == 'Active'
                ? AppColors.statusNormal
                : AppColors.statusOffline,
            icon: Icons.pets_outlined,
          ),
          if (row.openAlerts > 0)
            AdminStatusPill(
              label: '${row.openAlerts}',
              color: AppColors.statusWarning,
              icon: Icons.warning_amber_rounded,
            ),
        ],
        facts: [
          AdminKeyValue(label: l10n.adminColumnOwner, value: row.ownerUsername),
          AdminKeyValue(
            label: l10n.adminColumnBoard,
            value: row.devicePublicId ?? l10n.adminNoBoard,
            monospace: row.devicePublicId != null,
          ),
          if (row.deviceStatus != null)
            AdminKeyValue(
              label: l10n.adminColumnStatus,
              value: deviceStatusLabel(l10n, row.deviceStatus!),
              valueColor: deviceStatusPillColor(row.deviceStatus!),
            ),
          AdminKeyValue(
            label: l10n.adminColumnLastSeen,
            value: row.deviceLastSeenAt == null
                ? l10n.never
                : MetricFormat.dateTime(row.deviceLastSeenAt!),
          ),
          AdminKeyValue(
            label: l10n.adminColumnCreated,
            value: row.createdAt == null
                ? l10n.never
                : MetricFormat.dateTime(row.createdAt!),
          ),
        ],
      ),
    );
  }
}

/// Every board in the deployment, bound or not — an unclaimed board is exactly what an administrator looks for.
class _DeviceList extends StatefulWidget {
  const _DeviceList();

  @override
  State<_DeviceList> createState() => _DeviceListState();
}

class _DeviceListState extends State<_DeviceList> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        context.read<AdminProvider>().loadDevices();
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final admin = context.watch<AdminProvider>();
    final page = admin.devices;

    return _PagedList<AdminDevice>(
      state: page,
      searchHint: l10n.adminSearchDevices,
      onSearch: (value) => admin.loadDevices(search: value),
      onPrevious: () => admin.loadDevices(skip: page!.skip - adminPageSize),
      onNext: () => admin.loadDevices(skip: page!.skip + adminPageSize),
      onRetry: () => admin.loadDevices(skip: page?.skip ?? 0),
      rowBuilder: (context, row) => _RowCard(
        title: row.deviceName.isEmpty ? row.publicId : row.deviceName,
        subtitle: row.publicId,
        badges: [
          AdminStatusPill(
            label: deviceStatusLabel(l10n, row.status),
            color: deviceStatusPillColor(row.status),
            icon: Icons.memory_outlined,
          ),
        ],
        facts: [
          AdminKeyValue(
            label: l10n.adminColumnOwner,
            value: row.ownerUsername ?? l10n.never,
          ),
          AdminKeyValue(
            label: l10n.adminColumnTerrarium,
            value: row.terrariumName ?? l10n.adminNoBoard,
          ),
          AdminKeyValue(
            label: l10n.adminColumnFirmware,
            value: row.firmwareVersion.isEmpty ? l10n.never : row.firmwareVersion,
          ),
          AdminKeyValue(
            label: l10n.adminColumnLastSeen,
            value: row.lastSeenAt == null
                ? l10n.never
                : MetricFormat.dateTime(row.lastSeenAt!),
          ),
          AdminKeyValue(
            label: l10n.adminColumnSignal,
            value: row.signalStrengthDbm == null
                ? l10n.never
                : '${row.signalStrengthDbm} dBm',
          ),
          AdminKeyValue(
            label: l10n.adminColumnBattery,
            value: row.batteryPct == null
                ? l10n.never
                : '${row.batteryPct!.toStringAsFixed(0)} %',
          ),
        ],
      ),
    );
  }
}

/// Every account's alerts of both families, filtered by family and state.
class _AlertList extends StatefulWidget {
  const _AlertList();

  @override
  State<_AlertList> createState() => _AlertListState();
}

class _AlertListState extends State<_AlertList> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        context.read<AdminProvider>().loadAlerts();
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final admin = context.watch<AdminProvider>();
    final page = admin.alerts;

    return _PagedList<AdminAlert>(
      state: page,
      filters: Row(
        children: [
          Expanded(
            child: DropdownButtonFormField<String?>(
              initialValue: admin.alertFamily,
              decoration: const InputDecoration(
                isDense: true,
                labelText: null,
                border: OutlineInputBorder(),
              ),
              items: [
                DropdownMenuItem(
                  value: null,
                  child: Text(l10n.adminFilterFamilyAll),
                ),
                DropdownMenuItem(
                  value: 'enclosure',
                  child: Text(l10n.adminFamilyEnclosure),
                ),
                DropdownMenuItem(
                  value: 'species',
                  child: Text(l10n.adminFamilySpecies),
                ),
              ],
              onChanged: (value) =>
                  admin.filterAlerts(family: value, state: admin.alertState),
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: DropdownButtonFormField<String?>(
              initialValue: admin.alertState,
              decoration: const InputDecoration(
                isDense: true,
                border: OutlineInputBorder(),
              ),
              items: [
                DropdownMenuItem(
                  value: null,
                  child: Text(l10n.adminFilterStateAll),
                ),
                DropdownMenuItem(
                  value: 'Open',
                  child: Text(l10n.alertStateOpen),
                ),
                DropdownMenuItem(
                  value: 'Acknowledged',
                  child: Text(l10n.alertStateAcknowledged),
                ),
                DropdownMenuItem(
                  value: 'Resolved',
                  child: Text(l10n.alertStateResolved),
                ),
              ],
              onChanged: (value) =>
                  admin.filterAlerts(family: admin.alertFamily, state: value),
            ),
          ),
        ],
      ),
      onPrevious: () => admin.loadAlerts(skip: page!.skip - adminPageSize),
      onNext: () => admin.loadAlerts(skip: page!.skip + adminPageSize),
      onRetry: () => admin.loadAlerts(skip: page?.skip ?? 0),
      rowBuilder: (context, row) => _RowCard(
        title: row.metric == null
            ? alertSourceLabel(l10n, row.source)
            : metricDisplayName(l10n, row.metric!),
        subtitle: '${row.terrariumName} • ${row.ownerUsername}',
        badges: [
          AdminStatusPill(
            label: alertFamilyLabel(l10n, row.family),
            color: alertFamilyColor(row.family),
            icon: alertFamilyIcon(row.family),
          ),
          AdminStatusPill(
            label: severityLabel(l10n, row.severity),
            color: severityColor(row.severity),
          ),
          AdminStatusPill(
            label: alertStateLabel(l10n, row.state),
            color: row.isOpen
                ? AppColors.statusWarning
                : AppColors.statusNormal,
            icon: row.isOpen
                ? Icons.notifications_active_outlined
                : Icons.check_circle_outline,
          ),
        ],
        facts: [
          AdminKeyValue(
            label: l10n.adminColumnSource,
            value: alertSourceLabel(l10n, row.source),
          ),
          AdminKeyValue(
            label: l10n.adminColumnBoard,
            value: row.deviceId,
            monospace: true,
          ),
          AdminKeyValue(
            label: l10n.adminColumnTriggeredAt,
            value: row.triggeredAt == null
                ? l10n.never
                : MetricFormat.dateTime(row.triggeredAt!),
          ),
          AdminKeyValue(
            label: l10n.adminColumnResolvedAt,
            value: row.resolvedAt == null
                ? l10n.never
                : MetricFormat.dateTime(row.resolvedAt!),
          ),
        ],
      ),
    );
  }
}

/// Every species profile defined on the system (built-in and custom).
class _SpeciesProfileList extends StatefulWidget {
  const _SpeciesProfileList();

  @override
  State<_SpeciesProfileList> createState() => _SpeciesProfileListState();
}

class _SpeciesProfileListState extends State<_SpeciesProfileList> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        context.read<AdminProvider>().loadSpeciesProfiles();
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final admin = context.watch<AdminProvider>();
    final page = admin.speciesProfiles;

    return _PagedList<AdminSpeciesProfile>(
      state: page,
      searchHint: l10n.adminSearchSpeciesProfiles,
      onSearch: (value) => admin.loadSpeciesProfiles(search: value),
      onPrevious: () =>
          admin.loadSpeciesProfiles(skip: page!.skip - adminPageSize),
      onNext: () => admin.loadSpeciesProfiles(skip: page!.skip + adminPageSize),
      onRetry: () => admin.loadSpeciesProfiles(skip: page?.skip ?? 0),
      rowBuilder: (context, row) => _RowCard(
        title: row.name,
        subtitle: row.scientificName.isNotEmpty ? row.scientificName : null,
        badges: [
          AdminStatusPill(
            label: row.isBuiltIn
                ? l10n.speciesProfileBuiltIn
                : l10n.climateZoneSemiArid,
            color: row.isBuiltIn
                ? AppColors.primary
                : AppColors.statusNormal,
            icon: Icons.eco_outlined,
          ),
          AdminStatusPill(
            label: l10n.speciesProfileBandCount(row.bandsCount),
            color: AppColors.textMuted,
            icon: Icons.tune_outlined,
          ),
        ],
        facts: [
          AdminKeyValue(
            label: l10n.profileClimateZone,
            value: row.climateZone,
          ),
          AdminKeyValue(
            label: l10n.profilePhotoperiodHours,
            value: '${row.photoperiodHours.toStringAsFixed(1)} h',
          ),
          AdminKeyValue(
            label: l10n.profileLightsOn,
            value: row.lightsOnLocalTime,
          ),
          AdminKeyValue(
            label: l10n.profileLightThreshold,
            value: '${row.lightThresholdLux.toStringAsFixed(0)} lx',
          ),
          if (row.notes != null && row.notes!.isNotEmpty)
            AdminKeyValue(
              label: l10n.profileNotes,
              value: row.notes!,
            ),
        ],
      ),
    );
  }
}
