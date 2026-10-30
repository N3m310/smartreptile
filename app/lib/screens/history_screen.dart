import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../core/permissions.dart';
import '../core/problem.dart';
import '../l10n/generated/app_localizations.dart';
import '../l10n/labels.dart';
import '../models/api_terrarium_models.dart';
import '../state/session_provider.dart';
import '../state/terrarium_provider.dart';
import '../theme/app_theme.dart';

class HistoryScreen extends StatefulWidget {
  const HistoryScreen({super.key});

  @override
  State<HistoryScreen> createState() => _HistoryScreenState();
}

class _HistoryScreenState extends State<HistoryScreen> {
  String? _terrariumId;
  String _metric = 'tempC';
  int _hours = 24;
  DateTime _to = DateTime.now().toUtc();
  Future<Map<String, dynamic>>? _series;
  Future<Map<String, dynamic>>? _coverage;

  @override
  void initState() {
    super.initState();
    // Post-frame on purpose: `loadTerrariums` sets `_loading` and notifies synchronously before its first
    // `await`, and calling it from `initState` marks the `TerrariumProvider` scope dirty while the shell is
    // still building this screen ("setState() or markNeedsBuild() called during build").
    WidgetsBinding.instance.addPostFrameCallback((_) => _initialize());
  }

  Future<void> _initialize() async {
    final provider = context.read<TerrariumProvider>();
    await provider.loadTerrariums();
    if (!mounted) return;
    final rows = provider.terrariums;
    setState(() {
      _terrariumId ??= rows.isEmpty ? null : rows.first.id;
      _loadSeries();
    });
  }

  void _loadSeries() {
    final id = _terrariumId;
    if (id == null) {
      _series = null;
      _coverage = null;
      return;
    }
    final provider = context.read<TerrariumProvider>();
    final from = _to.subtract(Duration(hours: _hours));
    _series = provider.loadReadings(
      id: id,
      metric: _metric,
      from: from,
      to: _to,
    )..ignore();
    _coverage = provider.loadCoverage(id: id, from: from, to: _to)..ignore();
  }

  Future<void> _refresh() async {
    await context.read<TerrariumProvider>().loadTerrariums();
    if (!mounted) return;
    setState(() {
      _to = DateTime.now().toUtc();
      _loadSeries();
    });
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final session = context.watch<SessionProvider>();
    final isAdmin = (session.user?.role.toLowerCase() == 'administrator') ||
        (session.user?.role.toLowerCase() == 'admin') ||
        session.hasPermission(Permissions.dataReadAny) ||
        session.hasPermission(Permissions.userRead);
    final terrariums = context.watch<TerrariumProvider>().terrariums;
    final provider = context.watch<TerrariumProvider>();
    ApiTerrarium? selected;
    for (final terrarium in terrariums) {
      if (terrarium.id == _terrariumId) {
        selected = terrarium;
        break;
      }
    }

    return Scaffold(
      backgroundColor: AppColors.bgMain,
      appBar: isAdmin ? null : AppBar(title: Text(l10n.historyTitle)),
      body: RefreshIndicator(
        onRefresh: _refresh,
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.all(16),
          children: [
            if (provider.isLoading && terrariums.isEmpty)
              const LinearProgressIndicator(),
            if (provider.lastFailure != null && terrariums.isEmpty)
              _FailureCard(failure: provider.lastFailure!, retry: _initialize),
            if (terrariums.isEmpty && !provider.isLoading)
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(20),
                  child: Text(l10n.emptyStateTitle),
                ),
              )
            else if (terrariums.isNotEmpty) ...[
              DropdownButtonFormField<String>(
                initialValue: selected?.id,
                isExpanded: true,
                decoration: InputDecoration(labelText: l10n.terrariumDetail),
                items: [
                  for (final terrarium in terrariums)
                    DropdownMenuItem(
                      value: terrarium.id,
                      child: Text(
                        '${terrarium.name} · ${terrarium.speciesName ?? 'Chưa chọn loài'}',
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                ],
                onChanged: (id) {
                  if (id == null) return;
                  setState(() {
                    _terrariumId = id;
                    _loadSeries();
                  });
                },
              ),
              const SizedBox(height: 10),
              Wrap(
                spacing: 10,
                runSpacing: 8,
                children: [
                  DropdownButton<String>(
                    value: _metric,
                    items: [
                      DropdownMenuItem(
                        value: 'tempC',
                        child: Text('${l10n.metricTempC} (°C)'),
                      ),
                      DropdownMenuItem(
                        value: 'humidityPct',
                        child: Text('${l10n.metricHumidityPct} (%)'),
                      ),
                      DropdownMenuItem(
                        value: 'lightLux',
                        child: Text('${l10n.metricLightLux} (lx)'),
                      ),
                      DropdownMenuItem(
                        value: 'uvIndex',
                        child: Text(l10n.metricUvIndex),
                      ),
                      DropdownMenuItem(
                        value: 'surfaceTempC',
                        child: Text(l10n.metricSurfaceTempC),
                      ),
                    ],
                    onChanged: (metric) {
                      if (metric == null) return;
                      setState(() {
                        _metric = metric;
                        _loadSeries();
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
                    onChanged: (hours) {
                      if (hours == null) return;
                      setState(() {
                        _hours = hours;
                        _to = DateTime.now().toUtc();
                        _loadSeries();
                      });
                    },
                  ),
                ],
              ),
              const SizedBox(height: 8),
              _coverageCard(l10n),
              const SizedBox(height: 12),
              _seriesCard(l10n),
              if (selected != null && selected.device == null)
                Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: Text(
                    l10n.terrariumBindDeviceNone,
                    style: const TextStyle(color: AppColors.textMuted),
                  ),
                ),
            ],
          ],
        ),
      ),
    );
  }

  Widget _coverageCard(
    AppLocalizations l10n,
  ) => FutureBuilder<Map<String, dynamic>>(
    future: _coverage,
    builder: (context, snapshot) {
      if (snapshot.connectionState == ConnectionState.waiting) {
        return const Card(
          child: Padding(
            padding: EdgeInsets.all(14),
            child: LinearProgressIndicator(),
          ),
        );
      }
      if (snapshot.hasError || snapshot.data?['coveragePct'] == null) {
        return Card(
          child: ListTile(
            leading: const Icon(Icons.info_outline),
            title: Text(l10n.historyCoverageMissing),
          ),
        );
      }
      final coverage = (snapshot.data!['coveragePct'] as num).toDouble();
      return Card(
        child: ListTile(
          leading: const Icon(Icons.signal_cellular_alt),
          title: Text(l10n.historyCoverage(coverage.toStringAsFixed(0))),
          subtitle: Text(
            '${snapshot.data?['receivedSamples'] ?? 0} / ${snapshot.data?['expectedSamples'] ?? 0}',
          ),
        ),
      );
    },
  );

  Widget _seriesCard(AppLocalizations l10n) => Card(
    child: Padding(
      padding: const EdgeInsets.all(14),
      child: FutureBuilder<Map<String, dynamic>>(
        future: _series,
        builder: (context, snapshot) {
          if (snapshot.connectionState == ConnectionState.waiting) {
            return const SizedBox(
              height: 180,
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
          final validPoints = [
            for (final point in points)
              if (point['avg'] is num) point,
          ];
          if (validPoints.isEmpty) {
            return SizedBox(
              height: 100,
              child: Center(
                child: Text(
                  l10n.historyNoReadings,
                  textAlign: TextAlign.center,
                  style: const TextStyle(color: AppColors.textMuted),
                ),
              ),
            );
          }
          final spots = <FlSpot>[
            for (var index = 0; index < points.length; index++)
              points[index]['avg'] is num
                  ? FlSpot(
                      index.toDouble(),
                      (points[index]['avg'] as num).toDouble(),
                    )
                  : FlSpot.nullSpot,
          ];
          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                metricDisplayName(l10n, _metric),
                style: const TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.bold,
                  color: AppColors.textMain,
                ),
              ),
              const SizedBox(height: 16),
              SizedBox(
                height: 190,
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
              ),
              const Divider(height: 24),
              for (final point in validPoints.reversed.take(20))
                ListTile(
                  dense: true,
                  contentPadding: EdgeInsets.zero,
                  leading: const Icon(Icons.show_chart),
                  title: Text(_formatTime(point['t'] as String?)),
                  subtitle: Text(
                    '${l10n.historyMinimum}: ${_number(point['min'])} · '
                    '${l10n.historyAverage}: ${_number(point['avg'])} · '
                    '${l10n.historyMaximum}: ${_number(point['max'])}',
                  ),
                  trailing: Text('${point['count'] ?? 0}'),
                ),
            ],
          );
        },
      ),
    ),
  );

  String _formatTime(String? value) {
    final date = DateTime.tryParse(value ?? '')?.toLocal();
    return date == null ? '—' : DateFormat.MMMd().add_Hm().format(date);
  }

  String _number(Object? value) =>
      value is num ? value.toStringAsFixed(1) : '—';
}

class _FailureCard extends StatelessWidget {
  const _FailureCard({required this.failure, required this.retry});

  final AppFailure failure;
  final VoidCallback retry;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Card(
      child: ListTile(
        title: Text(failureMessage(l10n, failure)),
        trailing: IconButton(
          onPressed: retry,
          icon: const Icon(Icons.refresh),
          tooltip: l10n.retry,
        ),
      ),
    );
  }
}
