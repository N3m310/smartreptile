import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../core/problem.dart';
import '../core/permissions.dart';
import '../l10n/generated/app_localizations.dart';
import '../l10n/labels.dart';
import '../models/api_terrarium_models.dart';
import '../core/status.dart';
import '../state/device_provider.dart';
import '../state/session_provider.dart';
import '../state/terrarium_provider.dart';
import '../theme/app_theme.dart';

class TerrariumDetailScreen extends StatefulWidget {
  const TerrariumDetailScreen({super.key, required this.terrariumId});

  final String terrariumId;

  @override
  State<TerrariumDetailScreen> createState() => _TerrariumDetailScreenState();
}

class _TerrariumDetailScreenState extends State<TerrariumDetailScreen> {
  late Future<ApiTerrarium> _terrariumFuture;
  late Future<Map<String, dynamic>> _latestFuture;
  late DateTime _to;
  String _metric = 'tempC';
  int _hours = 24;
  Future<Map<String, dynamic>>? _seriesFuture;
  Future<Map<String, dynamic>>? _coverageFuture;

  @override
  void initState() {
    super.initState();
    _to = DateTime.now().toUtc();
    _load();
  }

  void _load() {
    final provider = context.read<TerrariumProvider>();
    _terrariumFuture = provider.loadTerrarium(widget.terrariumId);
    _latestFuture = provider.loadLatest(widget.terrariumId);
    _loadHistory();
  }

  void _loadHistory() {
    final provider = context.read<TerrariumProvider>();
    final from = _to.subtract(Duration(hours: _hours));
    _seriesFuture = provider.loadReadings(
      id: widget.terrariumId,
      metric: _metric,
      from: from,
      to: _to,
    );
    _coverageFuture = provider.loadCoverage(
      id: widget.terrariumId,
      from: from,
      to: _to,
    );
  }

  Future<void> _edit(ApiTerrarium current) async {
    final l10n = AppLocalizations.of(context);
    final updated = await showDialog<ApiTerrarium>(
      context: context,
      builder: (_) => _EditTerrariumDialog(current: current),
    );
    if (updated != null && mounted) {
      setState(() {
        _terrariumFuture = Future.value(updated);
      });
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(l10n.terrariumUpdated)));
    }
  }

  Future<void> _delete(ApiTerrarium current) async {
    final l10n = AppLocalizations.of(context);
    final accepted = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(l10n.terrariumDelete),
        content: Text(l10n.terrariumDeleteConfirm),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: Text(l10n.cancel),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: Text(l10n.terrariumDelete),
          ),
        ],
      ),
    );
    if (accepted != true || !mounted) return;
    try {
      await context.read<TerrariumProvider>().deleteTerrarium(current);
      if (mounted) Navigator.pop(context);
    } on AppFailure catch (failure) {
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(failureMessage(l10n, failure))));
    }
  }

  /// Offers the caller's unrevoked boards and binds the chosen one to this enclosure
  /// (`POST /devices/{id}/move`). A board that currently lives elsewhere is moved; a board already here is not
  /// offered, because the answer to that is the same enclosure it is in.
  Future<void> _pickDeviceToBind(AppLocalizations l10n) async {
    final deviceProvider = context.read<DeviceProvider>();
    await deviceProvider.loadDevices();
    if (!mounted) return;

    final candidates = deviceProvider.devices
        .where((device) => device.status != 'revoked' && device.terrariumId != widget.terrariumId)
        .toList();

    if (candidates.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Không có thiết bị nào để gắn. Hãy nhận thiết bị trước.')),
      );
      return;
    }

    String? selected = candidates.first.deviceId;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          backgroundColor: AppColors.bgCard,
          title: const Text('Gắn thiết bị vào bể', style: TextStyle(color: AppColors.textMain)),
          content: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                // The group owns the selection: per-tile `groupValue`/`onChanged` are deprecated in this Flutter.
                RadioGroup<String>(
                  groupValue: selected,
                  onChanged: (value) => setDialogState(() => selected = value),
                  child: Column(
                    children: [
                      for (final device in candidates)
                        RadioListTile<String>(
                          value: device.deviceId,
                          title: Text(
                            device.deviceName,
                            style: const TextStyle(color: AppColors.textMain),
                          ),
                          subtitle: Text(
                            device.terrariumName == null
                                ? device.deviceId
                                : '${device.deviceId} · đang ở ${device.terrariumName}',
                            style: const TextStyle(color: AppColors.textMuted, fontSize: 12),
                          ),
                        ),
                    ],
                  ),
                ),
                const SizedBox(height: 8),
                const Text(
                  'Mỗi bể nuôi chỉ chứa một thiết bị: thiết bị đang ở bể này (nếu có) sẽ được gỡ ra nhưng không bị thu hồi.',
                  style: TextStyle(color: AppColors.textMuted, fontSize: 11),
                ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: Text(l10n.cancel, style: const TextStyle(color: AppColors.textMuted)),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Gắn vào bể'),
            ),
          ],
        ),
      ),
    );

    if (confirmed != true || selected == null || !mounted) return;

    try {
      await deviceProvider.moveDevice(
        deviceId: selected!,
        terrariumId: widget.terrariumId,
      );
      if (!mounted) return;
      setState(_load);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Đã gắn thiết bị vào bể nuôi.')),
      );
    } on AppFailure catch (failure) {
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(failureMessage(l10n, failure))));
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    // Binding a board is `device.provision` — the same permission that claims one (07-appendices/03 §4.4). The
    // lookup is nullable so the screen still renders where no session provider is installed (widget tests, previews);
    // without one there is no permission to bind with.
    final canBindDevice =
        context.watch<SessionProvider?>()?.hasPermission(Permissions.deviceProvision) ?? false;
    return Scaffold(
      backgroundColor: AppColors.bgMain,
      appBar: AppBar(
        title: Text(l10n.terrariumDetail),
        actions: [
          FutureBuilder<ApiTerrarium>(
            future: _terrariumFuture,
            builder: (context, snapshot) {
              final current = snapshot.data;
              if (current == null) return const SizedBox.shrink();
              return PopupMenuButton<String>(
                onSelected: (action) {
                  if (action == 'edit') _edit(current);
                  if (action == 'delete') _delete(current);
                },
                itemBuilder: (context) => [
                  PopupMenuItem(value: 'edit', child: Text(l10n.terrariumEdit)),
                  PopupMenuItem(
                    value: 'delete',
                    child: Text(l10n.terrariumDelete),
                  ),
                ],
              );
            },
          ),
        ],
      ),
      body: FutureBuilder<ApiTerrarium>(
        future: _terrariumFuture,
        builder: (context, snapshot) {
          if (snapshot.connectionState == ConnectionState.waiting) {
            return const Center(child: CircularProgressIndicator());
          }
          if (snapshot.hasError) {
            final failure = snapshot.error is AppFailure
                ? snapshot.error as AppFailure
                : null;
            return _ErrorState(
              message: failure == null
                  ? l10n.errorGeneric
                  : failureMessage(l10n, failure),
              retry: () => setState(_load),
            );
          }
          final terrarium = snapshot.data;
          if (terrarium == null) {
            return Center(child: Text(l10n.terrariumNotFound));
          }
          return RefreshIndicator(
            onRefresh: () async => setState(_load),
            child: ListView(
              padding: const EdgeInsets.all(16),
              children: [
                Text(
                  terrarium.name,
                  style: const TextStyle(
                    color: AppColors.textMain,
                    fontSize: 22,
                    fontWeight: FontWeight.bold,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  terrarium.speciesName,
                  style: const TextStyle(color: AppColors.accent),
                ),
                if (terrarium.location?.isNotEmpty == true)
                  Text(
                    terrarium.location!,
                    style: const TextStyle(color: AppColors.textMuted),
                  ),
                // Only an administrator's answer carries the owner, whose list can hold enclosures belonging to
                // accounts they did not name.
                if (terrarium.ownerEmail != null) ...[
                  const SizedBox(height: 4),
                  Row(
                    children: [
                      const Icon(Icons.person_outline, size: 14, color: AppColors.textMuted),
                      const SizedBox(width: 4),
                      Expanded(
                        child: Text(
                          terrarium.ownerEmail!,
                          style: const TextStyle(color: AppColors.textMuted, fontSize: 12),
                        ),
                      ),
                    ],
                  ),
                ],
                const SizedBox(height: 18),
                _latestReadings(l10n),
                const SizedBox(height: 18),
                _history(l10n),
                const SizedBox(height: 12),
                if (terrarium.device != null)
                  Card(
                    child: ListTile(
                      leading: const Icon(Icons.memory),
                      title: Text(
                        terrarium.device?['deviceName'] as String? ??
                            l10n.device,
                      ),
                      subtitle: Text(
                        '${terrarium.device?['status'] ?? '—'} · ${terrarium.device?['deviceId'] ?? '—'}',
                      ),
                      trailing: canBindDevice
                          ? TextButton.icon(
                              onPressed: () => _pickDeviceToBind(l10n),
                              icon: const Icon(Icons.link, size: 16),
                              label: const Text('Đổi thiết bị'),
                            )
                          : null,
                    ),
                  )
                else
                  Card(
                    child: ListTile(
                      leading: const Icon(Icons.memory),
                      title: Text(l10n.device),
                      subtitle: Text(l10n.terrariumBindDeviceNone),
                      trailing: canBindDevice
                          ? TextButton.icon(
                              onPressed: () => _pickDeviceToBind(l10n),
                              icon: const Icon(Icons.add_link, size: 16),
                              label: const Text('Gắn thiết bị'),
                            )
                          : null,
                    ),
                  ),
              ],
            ),
          );
        },
      ),
    );
  }

  Widget _latestReadings(
    AppLocalizations l10n,
  ) => FutureBuilder<Map<String, dynamic>>(
    future: _latestFuture,
    builder: (context, snapshot) {
      if (snapshot.connectionState == ConnectionState.waiting) {
        return const Center(child: CircularProgressIndicator());
      }
      if (snapshot.hasError) {
        final failure = snapshot.error is AppFailure
            ? snapshot.error as AppFailure
            : null;
        return Text(
          failure == null ? l10n.errorGeneric : failureMessage(l10n, failure),
          style: const TextStyle(color: AppColors.statusDanger),
        );
      }
      final readings =
          (snapshot.data?['metrics'] as List?)
              ?.whereType<Map<String, dynamic>>()
              .toList() ??
          [];
      if (readings.isEmpty) {
        return Card(child: ListTile(title: Text(l10n.detailNoReadings)));
      }
      return Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            l10n.detailLatestReadings,
            style: const TextStyle(
              color: AppColors.textMain,
              fontSize: 16,
              fontWeight: FontWeight.bold,
            ),
          ),
          const SizedBox(height: 8),
          for (final reading in readings)
            Card(
              child: ListTile(
                leading: Icon(_metricIcon(reading['code'] as String?)),
                title: Text(
                  '${_metricLabel(l10n, reading['code'] as String?)}: ${reading['value'] ?? '—'} ${reading['unit'] ?? ''}',
                ),
                subtitle: Text(
                  '${MetricStatus.parse(reading['status'] as String?).label(l10n)} · ${_formatDate(reading['capturedAt'] as String?)}',
                ),
                trailing: _targetLabel(reading['target']),
              ),
            ),
        ],
      );
    },
  );

  Widget _history(AppLocalizations l10n) => Card(
    child: Padding(
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            l10n.historyTitle,
            style: const TextStyle(
              color: AppColors.textMain,
              fontWeight: FontWeight.bold,
              fontSize: 16,
            ),
          ),
          const SizedBox(height: 10),
          Wrap(
            spacing: 8,
            children: [
              DropdownButton<String>(
                value: _metric,
                items: const [
                  DropdownMenuItem(value: 'tempC', child: Text('°C')),
                  DropdownMenuItem(value: 'humidityPct', child: Text('%RH')),
                  DropdownMenuItem(value: 'lightLux', child: Text('lx')),
                  DropdownMenuItem(value: 'uvIndex', child: Text('UV')),
                  DropdownMenuItem(
                    value: 'surfaceTempC',
                    child: Text('°C surface'),
                  ),
                ],
                onChanged: (value) {
                  if (value == null) return;
                  setState(() {
                    _metric = value;
                    _loadHistory();
                  });
                },
              ),
              DropdownButton<int>(
                value: _hours,
                items: [
                  DropdownMenuItem(
                    value: 24,
                    child: Text(l10n.historyRange24h),
                  ),
                  DropdownMenuItem(
                    value: 168,
                    child: Text(l10n.historyRange7d),
                  ),
                ],
                onChanged: (value) {
                  if (value == null) return;
                  setState(() {
                    _hours = value;
                    _to = DateTime.now().toUtc();
                    _loadHistory();
                  });
                },
              ),
            ],
          ),
          FutureBuilder<Map<String, dynamic>>(
            future: _coverageFuture,
            builder: (context, snapshot) {
              if (snapshot.hasError) {
                return Padding(
                  padding: const EdgeInsets.only(bottom: 8),
                  child: Text(
                    l10n.historyCoverageMissing,
                    style: const TextStyle(color: AppColors.textMuted),
                  ),
                );
              }
              final coverage = snapshot.data?['coveragePct'];
              return coverage == null
                  ? const SizedBox.shrink()
                  : Padding(
                      padding: const EdgeInsets.only(bottom: 8),
                      child: Text(
                        l10n.historyCoverage(coverage.toString()),
                        style: const TextStyle(color: AppColors.textMuted),
                      ),
                    );
            },
          ),
          FutureBuilder<Map<String, dynamic>>(
            future: _seriesFuture,
            builder: (context, snapshot) {
              if (snapshot.connectionState == ConnectionState.waiting) {
                return const SizedBox(
                  height: 120,
                  child: Center(child: CircularProgressIndicator()),
                );
              }
              if (snapshot.hasError) {
                final failure = snapshot.error is AppFailure
                    ? snapshot.error as AppFailure
                    : null;
                return Text(
                  failure == null
                      ? l10n.errorGeneric
                      : failureMessage(l10n, failure),
                  style: const TextStyle(color: AppColors.statusDanger),
                );
              }
              final points =
                  (snapshot.data?['points'] as List?)
                      ?.whereType<Map<String, dynamic>>()
                      .toList() ??
                  [];
              final spots = <FlSpot>[
                for (var index = 0; index < points.length; index++)
                  points[index]['avg'] is num
                      ? FlSpot(
                          index.toDouble(),
                          (points[index]['avg'] as num).toDouble(),
                        )
                      : FlSpot.nullSpot,
              ];
              if (!spots.any((spot) => !spot.isNull())) {
                return Text(
                  l10n.historyNoReadings,
                  style: const TextStyle(color: AppColors.textMuted),
                );
              }
              return SizedBox(
                height: 180,
                child: LineChart(
                  LineChartData(
                    gridData: const FlGridData(show: true),
                    titlesData: const FlTitlesData(
                      topTitles: AxisTitles(
                        sideTitles: SideTitles(showTitles: false),
                      ),
                      rightTitles: AxisTitles(
                        sideTitles: SideTitles(showTitles: false),
                      ),
                    ),
                    borderData: FlBorderData(show: false),
                    lineBarsData: [
                      LineChartBarData(
                        spots: spots,
                        isCurved: true,
                        color: AppColors.primary,
                        dotData: const FlDotData(show: false),
                      ),
                    ],
                  ),
                ),
              );
            },
          ),
        ],
      ),
    ),
  );

  Widget _targetLabel(Object? target) {
    if (target is! Map<String, dynamic>) return const SizedBox.shrink();
    final min = target['min'];
    final max = target['max'];
    if (min == null && max == null) return const SizedBox.shrink();
    return Text(
      '${min ?? '—'}–${max ?? '—'}',
      style: const TextStyle(fontSize: 11),
    );
  }

  String _metricLabel(AppLocalizations l10n, String? code) => switch (code) {
    'tempC' => l10n.metricTempC,
    'humidityPct' => l10n.metricHumidityPct,
    'lightLux' => l10n.metricLightLux,
    'uvIndex' => l10n.metricUvIndex,
    'surfaceTempC' => l10n.metricSurfaceTempC,
    _ => l10n.device,
  };

  IconData _metricIcon(String? code) => switch (code) {
    'tempC' || 'surfaceTempC' => Icons.thermostat,
    'humidityPct' => Icons.water_drop,
    'lightLux' => Icons.wb_sunny_outlined,
    'uvIndex' => Icons.wb_sunny,
    _ => Icons.sensors,
  };

  String _formatDate(String? value) {
    if (value == null) return AppLocalizations.of(context).never;
    final date = DateTime.tryParse(value)?.toLocal();
    return date == null ? '—' : DateFormat.yMd().add_Hm().format(date);
  }
}

class _ErrorState extends StatelessWidget {
  const _ErrorState({required this.message, required this.retry});

  final String message;
  final VoidCallback retry;

  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(message, textAlign: TextAlign.center),
          const SizedBox(height: 8),
          TextButton(
            onPressed: retry,
            child: Text(AppLocalizations.of(context).retry),
          ),
        ],
      ),
    ),
  );
}

class _EditTerrariumDialog extends StatefulWidget {
  const _EditTerrariumDialog({required this.current});

  final ApiTerrarium current;

  @override
  State<_EditTerrariumDialog> createState() => _EditTerrariumDialogState();
}

class _EditTerrariumDialogState extends State<_EditTerrariumDialog> {
  late final TextEditingController _name;
  late final TextEditingController _location;
  late final TextEditingController _description;
  final _formKey = GlobalKey<FormState>();
  AppFailure? _failure;
  bool _saving = false;

  @override
  void initState() {
    super.initState();
    _name = TextEditingController(text: widget.current.name);
    _location = TextEditingController(text: widget.current.location ?? '');
    _description = TextEditingController(text: widget.current.description ?? '');
  }

  @override
  void dispose() {
    _name.dispose();
    _location.dispose();
    _description.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _saving = true;
      _failure = null;
    });

    try {
      final updated = await context.read<TerrariumProvider>().updateTerrarium(
        widget.current,
        name: _name.text,
        location: _location.text,
        description: _description.text,
      );
      if (mounted) {
        Navigator.pop(context, updated);
      }
    } on AppFailure catch (error) {
      if (mounted) {
        setState(() {
          _saving = false;
          _failure = error;
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() {
          _saving = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AlertDialog(
      title: Text(l10n.terrariumEdit),
      content: Form(
        key: _formKey,
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextFormField(
                controller: _name,
                maxLength: 60,
                decoration: InputDecoration(labelText: l10n.terrariumName),
                validator: (value) => value == null || value.trim().isEmpty
                    ? l10n.terrariumNameRequired
                    : value.trim().length > 60
                    ? l10n.terrariumNameTooLong
                    : null,
              ),
              TextFormField(
                controller: _location,
                maxLength: 120,
                decoration: InputDecoration(
                  labelText: l10n.terrariumLocation,
                ),
                validator: (value) => (value?.length ?? 0) > 120
                    ? l10n.terrariumLocationTooLong
                    : null,
              ),
              TextFormField(
                controller: _description,
                maxLength: 1000,
                maxLines: 3,
                decoration: InputDecoration(
                  labelText: l10n.terrariumDescription,
                ),
                validator: (value) => (value?.length ?? 0) > 1000
                    ? l10n.terrariumDescriptionTooLong
                    : null,
              ),
              if (_failure != null)
                Padding(
                  padding: const EdgeInsets.only(top: 10),
                  child: Text(
                    failureMessage(l10n, _failure!),
                    style: const TextStyle(color: AppColors.statusDanger),
                  ),
                ),
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: _saving ? null : () => Navigator.pop(context),
          child: Text(l10n.cancel),
        ),
        FilledButton(
          onPressed: _saving ? null : _submit,
          child: _saving
              ? const SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : Text(l10n.save),
        ),
      ],
    );
  }
}
