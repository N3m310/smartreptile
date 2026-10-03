import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../state/app_data_provider.dart';
import '../theme/app_theme.dart';

class AlertsScreen extends StatefulWidget {
  const AlertsScreen({super.key});

  @override
  State<AlertsScreen> createState() => _AlertsScreenState();
}

class _AlertsScreenState extends State<AlertsScreen> {
  String _filter = 'all'; // 'all', 'pending', 'resolved'

  IconData _getAlertIcon(String type) {
    switch (type) {
      case 'temp_high':
      case 'temp_low':
        return Icons.thermostat;
      case 'humidity_low':
      case 'humidity_high':
        return Icons.water_drop;
      case 'device_offline':
        return Icons.wifi_off;
      default:
        return Icons.warning_amber_rounded;
    }
  }

  @override
  Widget build(BuildContext context) {
    final data = context.watch<AppDataProvider>();

    final filtered = data.alerts.where((a) {
      if (_filter == 'pending') return !a.isResolved;
      if (_filter == 'resolved') return a.isResolved;
      return true;
    }).toList();

    return Scaffold(
      backgroundColor: AppColors.bgMain,
      body: Column(
        children: [
          // Filter Tabs
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
            decoration: const BoxDecoration(
              color: AppColors.bgCard,
              border: Border(bottom: BorderSide(color: AppColors.border)),
            ),
            child: Row(
              children: [
                _buildFilterChip('Tất cả (${data.alerts.length})', 'all'),
                const SizedBox(width: 8),
                _buildFilterChip(
                  'Chưa xử lý (${data.pendingAlertsCount})',
                  'pending',
                  activeColor: AppColors.statusDanger,
                ),
                const SizedBox(width: 8),
                _buildFilterChip(
                  'Đã xử lý (${data.alerts.length - data.pendingAlertsCount})',
                  'resolved',
                ),
              ],
            ),
          ),

          // Logic banner
          Container(
            width: double.infinity,
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            color: AppColors.bgMain,
            child: const Row(
              children: [
                Icon(Icons.shield_outlined,
                    size: 14, color: AppColors.primary),
                SizedBox(width: 6),
                Expanded(
                  child: Text(
                    'Logic ngưỡng: Kích hoạt khi giá trị vượt ngoài Min/Max (Không AI)',
                    style: TextStyle(color: AppColors.textMuted, fontSize: 10),
                  ),
                ),
              ],
            ),
          ),

          // Alerts List
          Expanded(
            child: filtered.isEmpty
                ? const Center(
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(Icons.check_circle_outline,
                            size: 48, color: AppColors.primary),
                        SizedBox(height: 12),
                        Text(
                          'Không có cảnh báo nào trong mục này',
                          style: TextStyle(
                            color: AppColors.textMain,
                            fontWeight: FontWeight.bold,
                            fontSize: 14,
                          ),
                        ),
                        SizedBox(height: 4),
                        Text(
                          'Môi trường vi khí hậu đang trong ngưỡng an toàn.',
                          style:
                              TextStyle(color: AppColors.textMuted, fontSize: 12),
                        ),
                      ],
                    ),
                  )
                : ListView.builder(
                    padding: const EdgeInsets.all(16),
                    itemCount: filtered.length,
                    itemBuilder: (context, index) {
                      final alert = filtered[index];
                      final isDanger = alert.severity == 'danger';

                      return Container(
                        margin: const EdgeInsets.only(bottom: 12),
                        padding: const EdgeInsets.all(16),
                        decoration: BoxDecoration(
                          color: alert.isResolved
                              ? AppColors.bgCard.withValues(alpha: 0.6)
                              : isDanger
                                  ? AppColors.statusDanger.withValues(alpha: 0.08)
                                  : AppColors.statusWarning.withValues(alpha: 0.08),
                          borderRadius: BorderRadius.circular(20),
                          border: Border.all(
                            color: alert.isResolved
                                ? AppColors.border
                                : isDanger
                                    ? AppColors.statusDanger.withValues(alpha: 0.5)
                                    : AppColors.statusWarning.withValues(alpha: 0.5),
                          ),
                        ),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Row(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Container(
                                  padding: const EdgeInsets.all(10),
                                  decoration: BoxDecoration(
                                    color: alert.isResolved
                                        ? AppColors.bgCardHover
                                        : isDanger
                                            ? AppColors.statusDanger
                                                .withValues(alpha: 0.2)
                                            : AppColors.statusWarning
                                                .withValues(alpha: 0.2),
                                    borderRadius: BorderRadius.circular(12),
                                  ),
                                  child: Icon(
                                    _getAlertIcon(alert.type),
                                    color: alert.isResolved
                                        ? AppColors.textMuted
                                        : isDanger
                                            ? AppColors.statusDanger
                                            : AppColors.statusWarning,
                                    size: 20,
                                  ),
                                ),
                                const SizedBox(width: 12),
                                Expanded(
                                  child: Column(
                                    crossAxisAlignment:
                                        CrossAxisAlignment.start,
                                    children: [
                                      Row(
                                        mainAxisAlignment:
                                            MainAxisAlignment.spaceBetween,
                                        children: [
                                          Container(
                                            padding: const EdgeInsets.symmetric(
                                                horizontal: 7, vertical: 2),
                                            decoration: BoxDecoration(
                                              color: alert.isResolved
                                                  ? AppColors.border
                                                  : isDanger
                                                      ? AppColors.statusDanger
                                                      : AppColors.statusWarning,
                                              borderRadius:
                                                  BorderRadius.circular(6),
                                            ),
                                            child: Text(
                                              alert.isResolved
                                                  ? 'ĐÃ XỬ LÝ'
                                                  : isDanger
                                                      ? 'NGUY HIỂM'
                                                      : 'CẢNH BÁO',
                                              style: TextStyle(
                                                color: isDanger || alert.isResolved
                                                    ? Colors.white
                                                    : Colors.black,
                                                fontWeight: FontWeight.bold,
                                                fontSize: 9,
                                              ),
                                            ),
                                          ),
                                          Text(
                                            alert.timestamp,
                                            style: const TextStyle(
                                              fontSize: 10,
                                              color: AppColors.textMuted,
                                            ),
                                          ),
                                        ],
                                      ),
                                      const SizedBox(height: 6),
                                      Text(
                                        alert.title,
                                        style: const TextStyle(
                                          fontWeight: FontWeight.bold,
                                          fontSize: 14,
                                          color: AppColors.textMain,
                                        ),
                                      ),
                                      const SizedBox(height: 3),
                                      Text(
                                        alert.message,
                                        style: const TextStyle(
                                          fontSize: 12,
                                          color: AppColors.textMuted,
                                        ),
                                      ),
                                      const SizedBox(height: 6),
                                      Text(
                                        alert.terrariumName,
                                        style: const TextStyle(
                                          color: AppColors.accent,
                                          fontWeight: FontWeight.w600,
                                          fontSize: 11,
                                        ),
                                      ),
                                    ],
                                  ),
                                ),
                              ],
                            ),

                            if (!alert.isResolved) ...[
                              const SizedBox(height: 12),
                              Align(
                                alignment: Alignment.centerRight,
                                child: ElevatedButton.icon(
                                  style: ElevatedButton.styleFrom(
                                    backgroundColor: AppColors.primary,
                                    foregroundColor: Colors.white,
                                    padding: const EdgeInsets.symmetric(
                                        horizontal: 14, vertical: 8),
                                    shape: RoundedRectangleBorder(
                                      borderRadius: BorderRadius.circular(10),
                                    ),
                                  ),
                                  onPressed: () {
                                    data.resolveAlert(alert.id);
                                    ScaffoldMessenger.of(context).showSnackBar(
                                      SnackBar(
                                        content: Text(
                                            'Đã xử lý cảnh báo ${alert.id}'),
                                        backgroundColor: AppColors.primary,
                                      ),
                                    );
                                  },
                                  icon: const Icon(Icons.check, size: 16),
                                  label: const Text(
                                    'Đã kiểm tra & Xử lý',
                                    style: TextStyle(
                                        fontSize: 12,
                                        fontWeight: FontWeight.bold),
                                  ),
                                ),
                              ),
                            ],
                          ],
                        ),
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }

  Widget _buildFilterChip(String label, String value, {Color? activeColor}) {
    final isSelected = _filter == value;
    final color = activeColor ?? AppColors.primary;

    return InkWell(
      borderRadius: BorderRadius.circular(10),
      onTap: () => setState(() => _filter = value),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
        decoration: BoxDecoration(
          color: isSelected ? color : AppColors.bgMain,
          borderRadius: BorderRadius.circular(10),
          border: Border.all(
            color: isSelected ? color : AppColors.border,
          ),
        ),
        child: Text(
          label,
          style: TextStyle(
            color: isSelected ? Colors.white : AppColors.textMuted,
            fontSize: 11,
            fontWeight: isSelected ? FontWeight.bold : FontWeight.normal,
          ),
        ),
      ),
    );
  }
}
