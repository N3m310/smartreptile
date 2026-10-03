import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../state/app_data_provider.dart';
import '../theme/app_theme.dart';

class HistoryScreen extends StatefulWidget {
  const HistoryScreen({super.key});

  @override
  State<HistoryScreen> createState() => _HistoryScreenState();
}

class _HistoryScreenState extends State<HistoryScreen> {
  String _selectedSensor = 'all'; // 'all', 'temp', 'humidity', 'light'

  @override
  Widget build(BuildContext context) {
    final data = context.watch<AppDataProvider>();
    final current = data.selectedTerrarium;
    final history = data.currentHistory;

    return Scaffold(
      backgroundColor: AppColors.bgMain,
      body: Column(
        children: [
          // Filter Toolbar
          Container(
            padding: const EdgeInsets.all(16),
            decoration: const BoxDecoration(
              color: AppColors.bgCard,
              border: Border(bottom: BorderSide(color: AppColors.border)),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    // Terrarium Dropdown
                    Expanded(
                      child: DropdownButtonHideUnderline(
                        child: DropdownButton<String>(
                          value: current.id,
                          isExpanded: true,
                          dropdownColor: AppColors.bgCardHover,
                          icon: const Icon(Icons.arrow_drop_down,
                              color: AppColors.primary),
                          style: const TextStyle(
                            color: AppColors.textMain,
                            fontWeight: FontWeight.bold,
                            fontSize: 13,
                          ),
                          items: [
                            for (final t in data.terrariums)
                              DropdownMenuItem(
                                value: t.id,
                                child: Text(
                                  '${t.name} (${t.species})',
                                  overflow: TextOverflow.ellipsis,
                                ),
                              ),
                          ],
                          onChanged: (id) {
                            if (id != null) data.selectTerrarium(id);
                          },
                        ),
                      ),
                    ),
                    const SizedBox(width: 10),

                    // Export CSV Button
                    OutlinedButton.icon(
                      style: OutlinedButton.styleFrom(
                        foregroundColor: AppColors.primary,
                        side: const BorderSide(color: AppColors.primary),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(10),
                        ),
                        padding: const EdgeInsets.symmetric(
                            horizontal: 10, vertical: 6),
                      ),
                      onPressed: () {
                        ScaffoldMessenger.of(context).showSnackBar(
                          SnackBar(
                            content: Text(
                                'Đã xuất dữ liệu CSV của ${current.name} thành công!'),
                            backgroundColor: AppColors.primary,
                          ),
                        );
                      },
                      icon: const Icon(Icons.download, size: 14),
                      label: const Text(
                        'Xuất CSV',
                        style: TextStyle(
                            fontSize: 11, fontWeight: FontWeight.bold),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                // Metric Filters
                SingleChildScrollView(
                  scrollDirection: Axis.horizontal,
                  child: Row(
                    children: [
                      _buildChip('Tất cả thông số', 'all'),
                      const SizedBox(width: 8),
                      _buildChip('Nhiệt độ (°C)', 'temp'),
                      const SizedBox(width: 8),
                      _buildChip('Độ ẩm (%)', 'humidity'),
                      const SizedBox(width: 8),
                      _buildChip('Ánh sáng (Lux)', 'light'),
                    ],
                  ),
                ),
              ],
            ),
          ),

          // History Log List
          Expanded(
            child: ListView.builder(
              padding: const EdgeInsets.all(16),
              itemCount: history.length,
              itemBuilder: (context, index) {
                final item = history[index];
                final isWarning = item.status == 'warning';
                final isDanger = item.status == 'danger';

                return Container(
                  margin: const EdgeInsets.only(bottom: 10),
                  padding:
                      const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                  decoration: BoxDecoration(
                    color: AppColors.bgCard,
                    borderRadius: BorderRadius.circular(16),
                    border: Border.all(
                      color: isDanger
                          ? AppColors.statusDanger.withValues(alpha: 0.5)
                          : isWarning
                              ? AppColors.statusWarning.withValues(alpha: 0.5)
                              : AppColors.border,
                    ),
                  ),
                  child: Row(
                    children: [
                      // Time badge
                      Container(
                        padding: const EdgeInsets.symmetric(
                            horizontal: 8, vertical: 4),
                        decoration: BoxDecoration(
                          color: AppColors.bgMain,
                          borderRadius: BorderRadius.circular(8),
                          border: Border.all(color: AppColors.border),
                        ),
                        child: Text(
                          item.time,
                          style: const TextStyle(
                            fontFamily: 'monospace',
                            fontSize: 11,
                            fontWeight: FontWeight.bold,
                            color: AppColors.textMain,
                          ),
                        ),
                      ),
                      const SizedBox(width: 14),

                      // Metrics
                      Expanded(
                        child: Wrap(
                          spacing: 12,
                          runSpacing: 4,
                          children: [
                            if (_selectedSensor == 'all' ||
                                _selectedSensor == 'temp')
                              Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  const Icon(Icons.thermostat,
                                      size: 14, color: AppColors.statusDanger),
                                  const SizedBox(width: 3),
                                  Text(
                                    '${item.temperature}°C',
                                    style: const TextStyle(
                                      fontWeight: FontWeight.bold,
                                      fontSize: 12,
                                      color: AppColors.textMain,
                                      fontFamily: 'monospace',
                                    ),
                                  ),
                                ],
                              ),
                            if (_selectedSensor == 'all' ||
                                _selectedSensor == 'humidity')
                              Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  const Icon(Icons.water_drop,
                                      size: 14, color: AppColors.primary),
                                  const SizedBox(width: 3),
                                  Text(
                                    '${item.humidity}%',
                                    style: const TextStyle(
                                      fontWeight: FontWeight.bold,
                                      fontSize: 12,
                                      color: AppColors.textMain,
                                      fontFamily: 'monospace',
                                    ),
                                  ),
                                ],
                              ),
                            if (_selectedSensor == 'all' ||
                                _selectedSensor == 'light')
                              Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  const Icon(Icons.wb_sunny_outlined,
                                      size: 14, color: AppColors.accent),
                                  const SizedBox(width: 3),
                                  Text(
                                    '${item.light} Lx',
                                    style: const TextStyle(
                                      fontWeight: FontWeight.bold,
                                      fontSize: 12,
                                      color: AppColors.textMain,
                                      fontFamily: 'monospace',
                                    ),
                                  ),
                                ],
                              ),
                          ],
                        ),
                      ),

                      // Status pill
                      Container(
                        padding: const EdgeInsets.symmetric(
                            horizontal: 6, vertical: 2),
                        decoration: BoxDecoration(
                          color: isDanger
                              ? AppColors.statusDanger.withValues(alpha: 0.15)
                              : isWarning
                                  ? AppColors.statusWarning
                                      .withValues(alpha: 0.15)
                                  : AppColors.primary.withValues(alpha: 0.15),
                          borderRadius: BorderRadius.circular(6),
                        ),
                        child: Text(
                          isDanger
                              ? 'Nguy hiểm'
                              : isWarning
                                  ? 'Cảnh báo'
                                  : 'Chuẩn',
                          style: TextStyle(
                            color: isDanger
                                ? AppColors.statusDanger
                                : isWarning
                                    ? AppColors.statusWarning
                                    : AppColors.primary,
                            fontSize: 9,
                            fontWeight: FontWeight.bold,
                          ),
                        ),
                      ),
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

  Widget _buildChip(String label, String value) {
    final isSelected = _selectedSensor == value;
    return InkWell(
      borderRadius: BorderRadius.circular(8),
      onTap: () => setState(() => _selectedSensor = value),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
        decoration: BoxDecoration(
          color: isSelected ? AppColors.primary : AppColors.bgMain,
          borderRadius: BorderRadius.circular(8),
          border: Border.all(
            color: isSelected ? AppColors.primary : AppColors.border,
          ),
        ),
        child: Text(
          label,
          style: TextStyle(
            fontSize: 10,
            color: isSelected ? Colors.white : AppColors.textMuted,
            fontWeight: isSelected ? FontWeight.bold : FontWeight.normal,
          ),
        ),
      ),
    );
  }
}
