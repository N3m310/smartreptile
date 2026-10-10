import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:fl_chart/fl_chart.dart';

import '../state/app_data_provider.dart';
import '../theme/app_theme.dart';
import 'terrarium_detail_screen.dart';

class DashboardScreen extends StatefulWidget {
  const DashboardScreen({super.key});

  @override
  State<DashboardScreen> createState() => _DashboardScreenState();
}

class _DashboardScreenState extends State<DashboardScreen> {
  bool _showTemp = true;
  bool _showHum = true;
  bool _showLight = false;

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
    final current = data.selectedTerrarium;
    final history = data.currentHistory;
    final recentAlerts = data.alerts
        .where((a) => !a.isResolved)
        .take(2)
        .toList();

    return Scaffold(
      backgroundColor: AppColors.bgMain,
      body: RefreshIndicator(
        color: AppColors.primary,
        onRefresh: () async {
          await Future.delayed(const Duration(milliseconds: 500));
        },
        child: ListView(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
          children: [
            // Top Terrarium Selector
            Container(
              padding: const EdgeInsets.all(16),
              decoration: BoxDecoration(
                color: AppColors.bgCard,
                borderRadius: BorderRadius.circular(20),
                border: Border.all(color: AppColors.border),
              ),
              child: Row(
                children: [
                  ClipRRect(
                    borderRadius: BorderRadius.circular(12),
                    child: Image.network(
                      current.image,
                      width: 50,
                      height: 50,
                      fit: BoxFit.cover,
                      errorBuilder: (context, error, stackTrace) => Container(
                        width: 50,
                        height: 50,
                        color: AppColors.bgCardHover,
                        child: const Icon(Icons.pets, color: AppColors.primary),
                      ),
                    ),
                  ),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Wrap(
                          spacing: 8,
                          runSpacing: 4,
                          children: [
                            Text(
                              current.name,
                              style: const TextStyle(
                                fontSize: 16,
                                fontWeight: FontWeight.bold,
                                color: AppColors.textMain,
                              ),
                            ),
                            const SizedBox(width: 8),
                            Container(
                              padding: const EdgeInsets.symmetric(
                                horizontal: 8,
                                vertical: 2,
                              ),
                              decoration: BoxDecoration(
                                color: _getStatusColor(current.status)
                                    .withValues(alpha: 0.2),
                                borderRadius: BorderRadius.circular(8),
                                border: Border.all(
                                  color: _getStatusColor(current.status),
                                  width: 0.8,
                                ),
                              ),
                              child: Text(
                                _getStatusText(current.status),
                                style: TextStyle(
                                  color: _getStatusColor(current.status),
                                  fontSize: 10,
                                  fontWeight: FontWeight.bold,
                                ),
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 2),
                        Text(
                          current.species,
                          style: const TextStyle(
                            fontSize: 12,
                            color: AppColors.textMuted,
                          ),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ],
                    ),
                  ),
                  PopupMenuButton<String>(
                    icon: const Icon(
                      Icons.swap_horiz,
                      color: AppColors.primary,
                    ),
                    color: AppColors.bgCardHover,
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(16),
                      side: const BorderSide(color: AppColors.border),
                    ),
                    onSelected: (id) => data.selectTerrarium(id),
                    itemBuilder: (context) => [
                      for (final t in data.terrariums)
                        PopupMenuItem(
                          value: t.id,
                          child: Text(
                            '${t.name} (${t.species})',
                            style: TextStyle(
                              color: t.id == current.id
                                  ? AppColors.primary
                                  : AppColors.textMain,
                              fontWeight: t.id == current.id
                                  ? FontWeight.bold
                                  : FontWeight.normal,
                              fontSize: 13,
                            ),
                          ),
                        ),
                    ],
                  ),
                ],
              ),
            ),

            const SizedBox(height: 16),

            // 4 Overview Stat Cards (Grid 2x2)
            Row(
              children: [
                Expanded(
                  child: _StatCard(
                    title: 'Terrarium',
                    value: '${data.terrariums.length}',
                    subtext: '3 chuồng đang chạy',
                    icon: Icons.grid_view,
                    iconColor: AppColors.primary,
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: _StatCard(
                    title: 'Thiết bị IoT',
                    value: '${data.onlineDevicesCount}/${data.devices.length}',
                    subtext: '1 node offline',
                    icon: Icons.memory,
                    iconColor: AppColors.accent,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: _StatCard(
                    title: 'Cảnh báo',
                    value: '${data.pendingAlertsCount}',
                    subtext: 'Chưa xử lý',
                    icon: Icons.warning_amber_rounded,
                    iconColor: AppColors.statusDanger,
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: _StatCard(
                    title: 'Hệ thống',
                    value: 'Ổn định',
                    subtext: 'MQTT: ~15ms',
                    icon: Icons.check_circle_outline,
                    iconColor: AppColors.primary,
                  ),
                ),
              ],
            ),

            const SizedBox(height: 20),

            // Section: Thông số vi khí hậu thời gian thực
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                const Expanded(
                  child: Text(
                    'Thông số vi khí hậu trực tiếp',
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontSize: 16,
                      fontWeight: FontWeight.bold,
                      color: AppColors.textMain,
                    ),
                  ),
                ),
                TextButton(
                  style: TextButton.styleFrom(
                    padding: EdgeInsets.zero,
                    minimumSize: const Size(48, 36),
                    tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                  ),
                  onPressed: () {
                    Navigator.push(
                      context,
                      MaterialPageRoute(
                        builder: (_) =>
                            TerrariumDetailScreen(terrarium: current),
                      ),
                    );
                  },
                  child: const Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        'Chi tiết',
                        style: TextStyle(
                          color: AppColors.primary,
                          fontSize: 13,
                        ),
                      ),
                      Icon(
                        Icons.chevron_right,
                        color: AppColors.primary,
                        size: 16,
                      ),
                    ],
                  ),
                ),
              ],
            ),

            const SizedBox(height: 8),

            // 3 Live Sensor Cards
            _SensorCard(
              title: 'Nhiệt độ',
              value: '${current.currentTemp}',
              unit: '°C',
              safeRange:
                  'Ngưỡng: ${current.thresholds.tempMin}°C - ${current.thresholds.tempMax}°C',
              icon: Icons.thermostat,
              color: AppColors.statusDanger,
              progress: ((current.currentTemp - 15) / (45 - 15)).clamp(
                0.0,
                1.0,
              ),
            ),
            const SizedBox(height: 10),
            _SensorCard(
              title: 'Độ ẩm không khí',
              value: '${current.currentHumidity}',
              unit: '%',
              safeRange:
                  'Ngưỡng: ${current.thresholds.humidityMin}% - ${current.thresholds.humidityMax}%',
              icon: Icons.water_drop,
              color: AppColors.primary,
              progress: (current.currentHumidity / 100.0).clamp(0.0, 1.0),
            ),
            const SizedBox(height: 10),
            _SensorCard(
              title: 'Cường độ ánh sáng',
              value: '${current.currentLight}',
              unit: 'Lux',
              safeRange:
                  'Ngưỡng: ${current.thresholds.lightMin} - ${current.thresholds.lightMax} Lux',
              icon: Icons.wb_sunny_outlined,
              color: AppColors.accent,
              progress: (current.currentLight / 1500.0).clamp(0.0, 1.0),
            ),

            const SizedBox(height: 24),

            // Line Chart 24h
            Container(
              padding: const EdgeInsets.all(16),
              decoration: BoxDecoration(
                color: AppColors.bgCard,
                borderRadius: BorderRadius.circular(20),
                border: Border.all(color: AppColors.border),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      const Text(
                        'Diễn biến 24h (Telemetry)',
                        style: TextStyle(
                          fontSize: 15,
                          fontWeight: FontWeight.bold,
                          color: AppColors.textMain,
                        ),
                      ),
                      Container(
                        padding: const EdgeInsets.symmetric(
                          horizontal: 8,
                          vertical: 3,
                        ),
                        decoration: BoxDecoration(
                          color: AppColors.bgMain,
                          borderRadius: BorderRadius.circular(8),
                          border: Border.all(color: AppColors.border),
                        ),
                        child: const Text(
                          '24 giờ qua',
                          style: TextStyle(
                            fontSize: 10,
                            color: AppColors.textMuted,
                          ),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 12),
                  // Toggles
                  Row(
                    children: [
                      FilterChip(
                        label: const Text('Nhiệt độ'),
                        selected: _showTemp,
                        onSelected: (val) => setState(() => _showTemp = val),
                        selectedColor: AppColors.statusDanger.withValues(
                          alpha: 0.3,
                        ),
                        checkmarkColor: AppColors.statusDanger,
                        labelStyle: TextStyle(
                          fontSize: 11,
                          color: _showTemp
                              ? AppColors.statusDanger
                              : AppColors.textMuted,
                          fontWeight: _showTemp
                              ? FontWeight.bold
                              : FontWeight.normal,
                        ),
                        backgroundColor: AppColors.bgMain,
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(8),
                        ),
                      ),
                      FilterChip(
                        label: const Text('Độ ẩm'),
                        selected: _showHum,
                        onSelected: (val) => setState(() => _showHum = val),
                        selectedColor: AppColors.primary.withValues(alpha: 0.3),
                        checkmarkColor: AppColors.primary,
                        labelStyle: TextStyle(
                          fontSize: 11,
                          color: _showHum
                              ? AppColors.primary
                              : AppColors.textMuted,
                          fontWeight: _showHum
                              ? FontWeight.bold
                              : FontWeight.normal,
                        ),
                        backgroundColor: AppColors.bgMain,
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(8),
                        ),
                      ),
                      FilterChip(
                        label: const Text('Ánh sáng'),
                        selected: _showLight,
                        onSelected: (val) => setState(() => _showLight = val),
                        selectedColor: AppColors.accent.withValues(alpha: 0.3),
                        checkmarkColor: AppColors.accent,
                        labelStyle: TextStyle(
                          fontSize: 11,
                          color: _showLight
                              ? AppColors.accent
                              : AppColors.textMuted,
                          fontWeight: _showLight
                              ? FontWeight.bold
                              : FontWeight.normal,
                        ),
                        backgroundColor: AppColors.bgMain,
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(8),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 18),
                  SizedBox(
                    height: 170,
                    child: LineChart(
                      LineChartData(
                        gridData: FlGridData(
                          show: true,
                          drawVerticalLine: false,
                          getDrawingHorizontalLine: (val) =>
                              FlLine(color: AppColors.border, strokeWidth: 1),
                        ),
                        titlesData: FlTitlesData(
                          rightTitles: const AxisTitles(
                            sideTitles: SideTitles(showTitles: false),
                          ),
                          topTitles: const AxisTitles(
                            sideTitles: SideTitles(showTitles: false),
                          ),
                          bottomTitles: AxisTitles(
                            sideTitles: SideTitles(
                              showTitles: true,
                              reservedSize: 22,
                              interval: 4,
                              getTitlesWidget: (value, meta) {
                                final index = value.toInt();
                                if (index >= 0 && index < history.length) {
                                  return Text(
                                    history[index].time,
                                    style: const TextStyle(
                                      color: AppColors.textMuted,
                                      fontSize: 9,
                                    ),
                                  );
                                }
                                return const SizedBox();
                              },
                            ),
                          ),
                          leftTitles: AxisTitles(
                            sideTitles: SideTitles(
                              showTitles: true,
                              reservedSize: 28,
                              getTitlesWidget: (value, meta) => Text(
                                value.toInt().toString(),
                                style: const TextStyle(
                                  color: AppColors.textMuted,
                                  fontSize: 9,
                                ),
                              ),
                            ),
                          ),
                        ),
                        borderData: FlBorderData(show: false),
                        lineBarsData: [
                          if (_showTemp)
                            LineChartBarData(
                              spots: [
                                for (int i = 0; i < history.length; i++)
                                  FlSpot(i.toDouble(), history[i].temperature),
                              ],
                              isCurved: true,
                              color: AppColors.statusDanger,
                              barWidth: 2.5,
                              dotData: const FlDotData(show: false),
                            ),
                          if (_showHum)
                            LineChartBarData(
                              spots: [
                                for (int i = 0; i < history.length; i++)
                                  FlSpot(i.toDouble(), history[i].humidity),
                              ],
                              isCurved: true,
                              color: AppColors.primary,
                              barWidth: 2.5,
                              dotData: const FlDotData(show: false),
                            ),
                          if (_showLight)
                            LineChartBarData(
                              spots: [
                                for (int i = 0; i < history.length; i++)
                                  FlSpot(
                                    i.toDouble(),
                                    history[i].light / 20.0,
                                  ), // Scale for chart
                              ],
                              isCurved: true,
                              color: AppColors.accent,
                              barWidth: 2,
                              dotData: const FlDotData(show: false),
                            ),
                        ],
                      ),
                    ),
                  ),
                ],
              ),
            ),

            const SizedBox(height: 24),

            // Recent Alerts
            if (recentAlerts.isNotEmpty) ...[
              const Text(
                'Cảnh báo gần đây',
                style: TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.bold,
                  color: AppColors.textMain,
                ),
              ),
              const SizedBox(height: 8),
              for (final alert in recentAlerts)
                Container(
                  margin: const EdgeInsets.only(bottom: 10),
                  padding: const EdgeInsets.all(14),
                  decoration: BoxDecoration(
                    color: alert.severity == 'danger'
                        ? AppColors.statusDanger.withValues(alpha: 0.1)
                        : AppColors.statusWarning.withValues(alpha: 0.1),
                    borderRadius: BorderRadius.circular(16),
                    border: Border.all(
                      color: alert.severity == 'danger'
                          ? AppColors.statusDanger.withValues(alpha: 0.4)
                          : AppColors.statusWarning.withValues(alpha: 0.4),
                    ),
                  ),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Icon(
                        Icons.warning_rounded,
                        color: alert.severity == 'danger'
                            ? AppColors.statusDanger
                            : AppColors.statusWarning,
                        size: 22,
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              alert.title,
                              style: const TextStyle(
                                fontWeight: FontWeight.bold,
                                color: AppColors.textMain,
                                fontSize: 13,
                              ),
                            ),
                            const SizedBox(height: 2),
                            Text(
                              alert.message,
                              style: const TextStyle(
                                color: AppColors.textMuted,
                                fontSize: 11,
                              ),
                            ),
                            const SizedBox(height: 4),
                            Text(
                              '${alert.terrariumName} • ${alert.timestamp}',
                              style: const TextStyle(
                                color: AppColors.primary,
                                fontSize: 10,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
            ],
            const SizedBox(height: 24),
          ],
        ),
      ),
    );
  }
}

class _StatCard extends StatelessWidget {
  const _StatCard({
    required this.title,
    required this.value,
    required this.subtext,
    required this.icon,
    required this.iconColor,
  });

  final String title;
  final String value;
  final String subtext;
  final IconData icon;
  final Color iconColor;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: AppColors.bgCard,
        borderRadius: BorderRadius.circular(16),
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
                style: const TextStyle(
                  fontSize: 11,
                  fontWeight: FontWeight.w600,
                  color: AppColors.textMuted,
                ),
              ),
              Icon(icon, color: iconColor, size: 18),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            value,
            style: const TextStyle(
              fontSize: 18,
              fontWeight: FontWeight.bold,
              color: AppColors.textMain,
            ),
          ),
          const SizedBox(height: 2),
          Text(
            subtext,
            style: const TextStyle(fontSize: 10, color: AppColors.textMuted),
          ),
        ],
      ),
    );
  }
}

class _SensorCard extends StatelessWidget {
  const _SensorCard({
    required this.title,
    required this.value,
    required this.unit,
    required this.safeRange,
    required this.icon,
    required this.color,
    required this.progress,
  });

  final String title;
  final String value;
  final String unit;
  final String safeRange;
  final IconData icon;
  final Color color;
  final double progress;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
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
              Expanded(
                child: Row(
                  children: [
                    Icon(icon, color: color, size: 18),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        title,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(
                          color: AppColors.textMuted,
                          fontSize: 12,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 8),
              Flexible(
                child: Text(
                  safeRange,
                  maxLines: 2,
                  textAlign: TextAlign.right,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(
                    color: AppColors.textMuted,
                    fontSize: 10,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Row(
            crossAxisAlignment: CrossAxisAlignment.baseline,
            textBaseline: TextBaseline.alphabetic,
            children: [
              Text(
                value,
                style: const TextStyle(
                  fontSize: 26,
                  fontWeight: FontWeight.bold,
                  color: AppColors.textMain,
                  fontFamily: 'monospace',
                ),
              ),
              const SizedBox(width: 4),
              Text(
                unit,
                style: const TextStyle(
                  fontSize: 14,
                  color: AppColors.textMuted,
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          ClipRRect(
            borderRadius: BorderRadius.circular(4),
            child: LinearProgressIndicator(
              value: progress,
              backgroundColor: AppColors.bgMain,
              valueColor: AlwaysStoppedAnimation(color),
              minHeight: 6,
            ),
          ),
        ],
      ),
    );
  }
}
