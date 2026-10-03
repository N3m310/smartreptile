import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:fl_chart/fl_chart.dart';
import '../models/terrarium_models.dart';
import '../state/app_data_provider.dart';
import '../theme/app_theme.dart';

class TerrariumDetailScreen extends StatelessWidget {
  const TerrariumDetailScreen({super.key, required this.terrarium});

  final TerrariumModel terrarium;

  Color _getStatusColor(String status) {
    switch (status) {
      case 'danger':
        return AppColors.statusDanger;
      case 'warning':
        return AppColors.statusWarning;
      default:
        return AppColors.statusNormal;
    }
  }

  String _getStatusText(String status) {
    switch (status) {
      case 'danger':
        return 'Nguy hiểm';
      case 'warning':
        return 'Cảnh báo';
      default:
        return 'Bình thường';
    }
  }

  @override
  Widget build(BuildContext context) {
    final data = context.watch<AppDataProvider>();
    final current = data.terrariums.firstWhere(
      (t) => t.id == terrarium.id,
      orElse: () => terrarium,
    );
    final devices =
        data.devices.where((d) => d.terrariumId == current.id).toList();
    final history = data.historyFor(current.id);

    return Scaffold(
      backgroundColor: AppColors.bgMain,
      appBar: AppBar(
        title: Text(current.name),
        actions: [
          Container(
            margin: const EdgeInsets.only(right: 16),
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
            decoration: BoxDecoration(
              color: _getStatusColor(current.status).withValues(alpha: 0.2),
              borderRadius: BorderRadius.circular(10),
              border: Border.all(color: _getStatusColor(current.status)),
            ),
            child: Text(
              _getStatusText(current.status),
              style: TextStyle(
                color: _getStatusColor(current.status),
                fontSize: 11,
                fontWeight: FontWeight.bold,
              ),
            ),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          // Hero Banner with Image
          ClipRRect(
            borderRadius: BorderRadius.circular(20),
            child: Stack(
              children: [
                Image.network(
                  current.image,
                  height: 180,
                  width: double.infinity,
                  fit: BoxFit.cover,
                  errorBuilder: (context, error, stackTrace) => Container(
                    height: 180,
                    color: AppColors.bgCardHover,
                    child: const Center(
                      child: Icon(Icons.pets, size: 48, color: AppColors.primary),
                    ),
                  ),
                ),
                Container(
                  height: 180,
                  decoration: BoxDecoration(
                    gradient: LinearGradient(
                      begin: Alignment.topCenter,
                      end: Alignment.bottomCenter,
                      colors: [
                        Colors.transparent,
                        AppColors.bgCard.withValues(alpha: 0.95),
                      ],
                    ),
                  ),
                ),
                Positioned(
                  bottom: 14,
                  left: 16,
                  right: 16,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        current.species,
                        style: const TextStyle(
                          color: AppColors.accent,
                          fontWeight: FontWeight.bold,
                          fontSize: 14,
                        ),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        current.description,
                        style: const TextStyle(
                          color: AppColors.textMuted,
                          fontSize: 11,
                        ),
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),

          const SizedBox(height: 18),

          // 3 Sensor Gauge Cards
          Row(
            children: [
              Expanded(
                child: _GaugeItem(
                  title: 'Nhiệt độ',
                  value: '${current.currentTemp}°C',
                  range: '${current.thresholds.tempMin}-${current.thresholds.tempMax}°C',
                  color: AppColors.statusDanger,
                  icon: Icons.thermostat,
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: _GaugeItem(
                  title: 'Độ ẩm',
                  value: '${current.currentHumidity}%',
                  range: '${current.thresholds.humidityMin}-${current.thresholds.humidityMax}%',
                  color: AppColors.primary,
                  icon: Icons.water_drop,
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: _GaugeItem(
                  title: 'Ánh sáng',
                  value: '${current.currentLight} Lx',
                  range: '${current.thresholds.lightMin}-${current.thresholds.lightMax} Lx',
                  color: AppColors.accent,
                  icon: Icons.wb_sunny_outlined,
                ),
              ),
            ],
          ),

          const SizedBox(height: 24),

          // Detailed Charts
          const Text(
            'Biểu đồ chi tiết theo ngưỡng (24h)',
            style: TextStyle(
              fontSize: 16,
              fontWeight: FontWeight.bold,
              color: AppColors.textMain,
            ),
          ),
          const SizedBox(height: 12),

          // 1. Temp Chart with reference lines
          _SingleChartCard(
            title: 'Nhiệt độ (°C)',
            color: AppColors.statusDanger,
            minThreshold: current.thresholds.tempMin,
            maxThreshold: current.thresholds.tempMax,
            spots: [
              for (int i = 0; i < history.length; i++)
                FlSpot(i.toDouble(), history[i].temperature),
            ],
            history: history,
          ),

          const SizedBox(height: 14),

          // 2. Humidity Chart
          _SingleChartCard(
            title: 'Độ ẩm (%)',
            color: AppColors.primary,
            minThreshold: current.thresholds.humidityMin,
            maxThreshold: current.thresholds.humidityMax,
            spots: [
              for (int i = 0; i < history.length; i++)
                FlSpot(i.toDouble(), history[i].humidity),
            ],
            history: history,
          ),

          const SizedBox(height: 24),

          // Connected Devices List
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              const Text(
                'Thiết bị kết nối với chuồng',
                style: TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.bold,
                  color: AppColors.textMain,
                ),
              ),
              Text(
                '${devices.length} thiết bị',
                style: const TextStyle(fontSize: 12, color: AppColors.textMuted),
              ),
            ],
          ),
          const SizedBox(height: 10),

          for (final d in devices)
            Container(
              margin: const EdgeInsets.only(bottom: 8),
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: AppColors.bgCard,
                borderRadius: BorderRadius.circular(16),
                border: Border.all(color: AppColors.border),
              ),
              child: Row(
                children: [
                  Container(
                    padding: const EdgeInsets.all(8),
                    decoration: BoxDecoration(
                      color: AppColors.bgMain,
                      borderRadius: BorderRadius.circular(10),
                      border: Border.all(color: AppColors.border),
                    ),
                    child: const Icon(Icons.memory,
                        size: 20, color: AppColors.primary),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          d.name,
                          style: const TextStyle(
                            fontWeight: FontWeight.bold,
                            color: AppColors.textMain,
                            fontSize: 13,
                          ),
                        ),
                        Text(
                          '${d.type} • FW ${d.firmware}',
                          style: const TextStyle(
                            color: AppColors.textMuted,
                            fontSize: 11,
                          ),
                        ),
                      ],
                    ),
                  ),
                  Container(
                    padding:
                        const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                    decoration: BoxDecoration(
                      color: d.isOnline
                          ? AppColors.primary.withValues(alpha: 0.15)
                          : AppColors.statusDanger.withValues(alpha: 0.15),
                      borderRadius: BorderRadius.circular(6),
                      border: Border.all(
                        color: d.isOnline
                            ? AppColors.primary
                            : AppColors.statusDanger,
                        width: 0.8,
                      ),
                    ),
                    child: Text(
                      d.isOnline ? 'Online' : 'Offline',
                      style: TextStyle(
                        color: d.isOnline
                            ? AppColors.primary
                            : AppColors.statusDanger,
                        fontSize: 10,
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          const SizedBox(height: 24),
        ],
      ),
    );
  }
}

class _GaugeItem extends StatelessWidget {
  const _GaugeItem({
    required this.title,
    required this.value,
    required this.range,
    required this.color,
    required this.icon,
  });

  final String title;
  final String value;
  final String range;
  final Color color;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    return Container(
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
              Icon(icon, color: color, size: 14),
              const SizedBox(width: 4),
              Text(
                title,
                style: const TextStyle(
                  color: AppColors.textMuted,
                  fontSize: 11,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            value,
            style: const TextStyle(
              fontSize: 16,
              fontWeight: FontWeight.bold,
              color: AppColors.textMain,
              fontFamily: 'monospace',
            ),
          ),
          const SizedBox(height: 2),
          Text(
            range,
            style: const TextStyle(
              fontSize: 9,
              color: AppColors.textMuted,
            ),
          ),
        ],
      ),
    );
  }
}

class _SingleChartCard extends StatelessWidget {
  const _SingleChartCard({
    required this.title,
    required this.color,
    required this.minThreshold,
    required this.maxThreshold,
    required this.spots,
    required this.history,
  });

  final String title;
  final Color color;
  final double minThreshold;
  final double maxThreshold;
  final List<FlSpot> spots;
  final List<HistoryPoint> history;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: AppColors.bgCard,
        borderRadius: BorderRadius.circular(18),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                title,
                style: TextStyle(
                  color: color,
                  fontWeight: FontWeight.bold,
                  fontSize: 13,
                ),
              ),
              Text(
                'Min: $minThreshold • Max: $maxThreshold',
                style: const TextStyle(fontSize: 10, color: AppColors.textMuted),
              ),
            ],
          ),
          const SizedBox(height: 12),
          SizedBox(
            height: 120,
            child: LineChart(
              LineChartData(
                gridData: const FlGridData(show: false),
                titlesData: FlTitlesData(
                  rightTitles:
                      const AxisTitles(sideTitles: SideTitles(showTitles: false)),
                  topTitles:
                      const AxisTitles(sideTitles: SideTitles(showTitles: false)),
                  bottomTitles: AxisTitles(
                    sideTitles: SideTitles(
                      showTitles: true,
                      reservedSize: 18,
                      interval: 6,
                      getTitlesWidget: (val, _) {
                        final idx = val.toInt();
                        if (idx >= 0 && idx < history.length) {
                          return Text(
                            history[idx].time,
                            style: const TextStyle(
                                fontSize: 8, color: AppColors.textMuted),
                          );
                        }
                        return const SizedBox();
                      },
                    ),
                  ),
                  leftTitles: AxisTitles(
                    sideTitles: SideTitles(
                      showTitles: true,
                      reservedSize: 24,
                      getTitlesWidget: (val, _) => Text(
                        val.toInt().toString(),
                        style: const TextStyle(
                            fontSize: 8, color: AppColors.textMuted),
                      ),
                    ),
                  ),
                ),
                borderData: FlBorderData(show: false),
                extraLinesData: ExtraLinesData(
                  horizontalLines: [
                    HorizontalLine(
                      y: maxThreshold,
                      color: AppColors.statusDanger.withValues(alpha: 0.6),
                      strokeWidth: 1.5,
                      dashArray: [4, 4],
                      label: HorizontalLineLabel(
                        show: true,
                        labelResolver: (_) => 'Max',
                        style: const TextStyle(
                            color: AppColors.statusDanger, fontSize: 8),
                      ),
                    ),
                    HorizontalLine(
                      y: minThreshold,
                      color: AppColors.statusWarning.withValues(alpha: 0.6),
                      strokeWidth: 1.5,
                      dashArray: [4, 4],
                      label: HorizontalLineLabel(
                        show: true,
                        labelResolver: (_) => 'Min',
                        style: const TextStyle(
                            color: AppColors.statusWarning, fontSize: 8),
                      ),
                    ),
                  ],
                ),
                lineBarsData: [
                  LineChartBarData(
                    spots: spots,
                    isCurved: true,
                    color: color,
                    barWidth: 2,
                    dotData: const FlDotData(show: false),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
