import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/formatting.dart';
import '../../core/permissions.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../l10n/labels.dart';
import '../../models/admin_models.dart';
import '../../state/admin_provider.dart';
import '../../theme/app_theme.dart';
import 'admin_widgets.dart';

/// The administrator's dashboard: the deployment's figures, the two alert families, and who has what.
///
/// One call, because the screen is one answer — the counts, the rollup and the family descriptions come from
/// `GET /api/v1/admin/overview`, which exists so a header does not become eight requests.
class AdminOverviewScreen extends StatefulWidget {
  /// Creates the screen.
  const AdminOverviewScreen({super.key});

  @override
  State<AdminOverviewScreen> createState() => _AdminOverviewScreenState();
}

class _AdminOverviewScreenState extends State<AdminOverviewScreen> {
  @override
  void initState() {
    super.initState();
    // After the first frame: reading in `initState` would notify the provider while this screen is still being built.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        context.read<AdminProvider>().loadOverview();
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return Scaffold(
      backgroundColor: AppColors.bgMain,
      appBar: AppBar(title: Text(l10n.adminOverviewEntry)),
      body: AdminGate(
        permission: Permissions.dataReadAny,
        child: Consumer<AdminProvider>(
          builder: (context, admin, _) {
            final overview = admin.overview;

            if (overview == null) {
              if (admin.isLoadingOverview) {
                return const AdminLoading();
              }
              if (admin.overviewFailure != null) {
                return SingleChildScrollView(
                  padding: const EdgeInsets.all(16),
                  child: AdminErrorView(
                    failure: admin.overviewFailure!,
                    onRetry: admin.loadOverview,
                  ),
                );
              }
              return const AdminLoading();
            }

            return RefreshIndicator(
              onRefresh: admin.loadOverview,
              child: ListView(
                padding: const EdgeInsets.all(16),
                children: [
                  if (admin.overviewFailure != null)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 12),
                      child: AdminErrorView(
                        failure: admin.overviewFailure!,
                        onRetry: admin.loadOverview,
                      ),
                    ),
                  _CountsSection(counts: overview.counts),
                  _FamiliesSection(families: overview.families),
                  _RollupSection(accounts: overview.accounts),
                ],
              ),
            );
          },
        ),
      ),
    );
  }
}

/// The header's figures (FR-18).
class _CountsSection extends StatelessWidget {
  const _CountsSection({required this.counts});

  final AdminCounts counts;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return AdminSection(
      title: l10n.adminSectionCounts,
      child: Wrap(
        spacing: 12,
        runSpacing: 12,
        children: [
          AdminCountTile(
            label: l10n.adminCountAccounts,
            value: '${counts.accounts}',
            icon: Icons.people_outline,
            color: AppColors.primary,
          ),
          AdminCountTile(
            label: l10n.adminCountAccountsActive,
            value: '${counts.accountsActive}',
            icon: Icons.how_to_reg_outlined,
            color: AppColors.primary,
          ),
          AdminCountTile(
            label: l10n.adminCountTerrariums,
            value: '${counts.terrariums}',
            icon: Icons.pets_outlined,
            color: AppColors.primary,
          ),
          AdminCountTile(
            label: l10n.adminCountTerrariumsWithDevice,
            value: '${counts.terrariumsWithDevice}',
            icon: Icons.link,
            color: AppColors.primary,
          ),
          AdminCountTile(
            label: l10n.adminCountDevices,
            value: '${counts.devices}',
            icon: Icons.memory_outlined,
            color: AppColors.accent,
          ),
          AdminCountTile(
            label: l10n.adminCountDevicesOnline,
            value: '${counts.devicesOnline}',
            icon: Icons.wifi_tethering,
            color: AppColors.statusNormal,
          ),
          AdminCountTile(
            label: l10n.adminCountAlertsOpen,
            value: '${counts.alertsOpen}',
            icon: Icons.warning_amber_rounded,
            color: AppColors.statusWarning,
          ),
          AdminCountTile(
            label: l10n.adminCountAlertsCritical,
            value: '${counts.alertsCritical}',
            icon: Icons.error_outline,
            color: AppColors.statusDanger,
          ),
          AdminCountTile(
            label: l10n.adminCountSpeciesProfiles,
            value: '${counts.speciesProfiles}',
            icon: Icons.science_outlined,
            color: AppColors.accent,
          ),
        ],
      ),
    );
  }
}

/// The two alert families, kept visibly apart — the product decision this screen has to carry.
class _FamiliesSection extends StatelessWidget {
  const _FamiliesSection({required this.families});

  final List<AdminFamily> families;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return AdminSection(
      title: l10n.adminSectionFamilies,
      caption: l10n.adminFamiliesCaption,
      child: Column(
        children: [
          for (final family in families) _FamilyCard(family: family),
        ],
      ),
    );
  }
}

class _FamilyCard extends StatelessWidget {
  const _FamilyCard({required this.family});

  final AdminFamily family;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final color = alertFamilyColor(family.key);

    return Container(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: AppColors.bgMain,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(alertFamilyIcon(family.key), size: 16, color: color),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  alertFamilyLabel(l10n, family.key),
                  style: const TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.bold,
                    color: AppColors.textMain,
                  ),
                ),
              ),
              AdminStatusPill(
                label: l10n.adminFamilyOpen(family.alertsOpen),
                color: family.alertsOpen == 0
                    ? AppColors.textMuted
                    : AppColors.statusWarning,
                icon: Icons.notifications_active_outlined,
              ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            alertFamilyDetail(l10n, family.key),
            style: const TextStyle(
              fontSize: 11,
              height: 1.35,
              color: AppColors.textMuted,
            ),
          ),
          const SizedBox(height: 6),
          Text(
            l10n.adminFamilyMetrics(metricListLabel(l10n, family.metrics)),
            style: const TextStyle(fontSize: 10, color: AppColors.textMuted),
          ),
        ],
      ),
    );
  }
}

/// The rollup: every account with what it holds. A table rather than cards, because the question is a comparison.
class _RollupSection extends StatelessWidget {
  const _RollupSection({required this.accounts});

  final List<AdminAccountRollup> accounts;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return AdminSection(
      title: l10n.adminSectionRollup,
      child: accounts.isEmpty
          ? AdminEmptyView(message: l10n.adminRollupEmpty)
          : SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: DataTable(
                columnSpacing: 18,
                headingTextStyle: const TextStyle(
                  fontSize: 11,
                  fontWeight: FontWeight.bold,
                  color: AppColors.textMuted,
                ),
                dataTextStyle: const TextStyle(
                  fontSize: 11,
                  color: AppColors.textMain,
                ),
                columns: [
                  DataColumn(label: Text(l10n.adminColumnAccount)),
                  DataColumn(label: Text(l10n.adminColumnRole)),
                  DataColumn(label: Text(l10n.adminColumnState)),
                  DataColumn(label: Text(l10n.adminColumnTerrariums)),
                  DataColumn(label: Text(l10n.adminColumnDevices)),
                  DataColumn(label: Text(l10n.adminColumnAlertsOpen)),
                  DataColumn(label: Text(l10n.adminColumnLastLogin)),
                ],
                rows: [
                  for (final account in accounts)
                    DataRow(
                      cells: [
                        DataCell(
                          Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            mainAxisAlignment: MainAxisAlignment.center,
                            children: [
                              Text(
                                account.username,
                                style: const TextStyle(
                                  fontWeight: FontWeight.bold,
                                ),
                              ),
                              Text(
                                account.email,
                                style: const TextStyle(
                                  fontSize: 10,
                                  color: AppColors.textMuted,
                                ),
                              ),
                            ],
                          ),
                        ),
                        DataCell(
                          Text(
                            account.role,
                            style: const TextStyle(fontFamily: 'monospace'),
                          ),
                        ),
                        DataCell(
                          AdminStatusPill(
                            label: account.isActive
                                ? l10n.adminBadgeActive
                                : l10n.adminBadgeDisabled,
                            color: account.isActive
                                ? AppColors.statusNormal
                                : AppColors.statusOffline,
                            icon: account.isActive
                                ? Icons.check_circle_outline
                                : Icons.block,
                          ),
                        ),
                        DataCell(Text('${account.terrariums}')),
                        DataCell(Text('${account.devices}')),
                        DataCell(
                          Text(
                            '${account.alertsOpen}',
                            style: TextStyle(
                              color: account.alertsOpen == 0
                                  ? AppColors.textMuted
                                  : AppColors.statusWarning,
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                        ),
                        DataCell(
                          Text(
                            account.lastLoginAt == null
                                ? l10n.never
                                : MetricFormat.dateTime(account.lastLoginAt!),
                          ),
                        ),
                      ],
                    ),
                ],
              ),
            ),
    );
  }
}
