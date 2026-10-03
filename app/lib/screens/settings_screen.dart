import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../models/terrarium_models.dart';
import '../state/app_data_provider.dart';
import '../theme/app_theme.dart';

class SettingsScreen extends StatefulWidget {
  const SettingsScreen({super.key});

  @override
  State<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends State<SettingsScreen> {
  bool _notifyTemp = true;
  bool _notifyHumidity = true;
  bool _notifyOffline = true;

  late String _currentTerrariumId;
  late TextEditingController _tempMinCtrl;
  late TextEditingController _tempMaxCtrl;
  late TextEditingController _humMinCtrl;
  late TextEditingController _humMaxCtrl;
  late TextEditingController _lightMinCtrl;
  late TextEditingController _lightMaxCtrl;

  @override
  void initState() {
    super.initState();
    _currentTerrariumId = 'T01';
    _tempMinCtrl = TextEditingController(text: '28.0');
    _tempMaxCtrl = TextEditingController(text: '38.0');
    _humMinCtrl = TextEditingController(text: '30.0');
    _humMaxCtrl = TextEditingController(text: '45.0');
    _lightMinCtrl = TextEditingController(text: '500');
    _lightMaxCtrl = TextEditingController(text: '1200');
  }

  void _loadTerrariumThresholds(TerrariumModel t) {
    setState(() {
      _currentTerrariumId = t.id;
      _tempMinCtrl.text = t.thresholds.tempMin.toString();
      _tempMaxCtrl.text = t.thresholds.tempMax.toString();
      _humMinCtrl.text = t.thresholds.humidityMin.toString();
      _humMaxCtrl.text = t.thresholds.humidityMax.toString();
      _lightMinCtrl.text = t.thresholds.lightMin.toString();
      _lightMaxCtrl.text = t.thresholds.lightMax.toString();
    });
  }

  @override
  void dispose() {
    _tempMinCtrl.dispose();
    _tempMaxCtrl.dispose();
    _humMinCtrl.dispose();
    _humMaxCtrl.dispose();
    _lightMinCtrl.dispose();
    _lightMaxCtrl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final data = context.watch<AppDataProvider>();
    final devices = data.devices;

    return Scaffold(
      backgroundColor: AppColors.bgMain,
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          // 1. Profile Card
          Container(
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: AppColors.bgCard,
              borderRadius: BorderRadius.circular(20),
              border: Border.all(color: AppColors.border),
            ),
            child: Row(
              children: [
                CircleAvatar(
                  radius: 26,
                  backgroundColor: AppColors.primary.withValues(alpha: 0.2),
                  child: const Icon(Icons.person,
                      color: AppColors.primary, size: 28),
                ),
                const SizedBox(width: 14),
                const Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'Nguyễn Văn Quản Trị',
                        style: TextStyle(
                          fontWeight: FontWeight.bold,
                          fontSize: 16,
                          color: AppColors.textMain,
                        ),
                      ),
                      SizedBox(height: 2),
                      Text(
                        'admin@terraguard.vn • 0987 654 321',
                        style: TextStyle(
                            fontSize: 12, color: AppColors.textMuted),
                      ),
                    ],
                  ),
                ),
                IconButton(
                  icon: const Icon(Icons.edit_outlined,
                      color: AppColors.textMuted, size: 20),
                  onPressed: () {
                    ScaffoldMessenger.of(context).showSnackBar(
                      const SnackBar(
                        content: Text('Hồ sơ đã được đồng bộ với hệ thống.'),
                      ),
                    );
                  },
                ),
              ],
            ),
          ),

          const SizedBox(height: 20),

          // 2. Threshold Configuration Section
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
                      'Cấu hình Ngưỡng Vi khí hậu',
                      style: TextStyle(
                        fontSize: 15,
                        fontWeight: FontWeight.bold,
                        color: AppColors.textMain,
                      ),
                    ),
                    DropdownButtonHideUnderline(
                      child: DropdownButton<String>(
                        value: _currentTerrariumId,
                        dropdownColor: AppColors.bgCardHover,
                        style: const TextStyle(
                          color: AppColors.primary,
                          fontWeight: FontWeight.bold,
                          fontSize: 12,
                        ),
                        items: [
                          for (final t in data.terrariums)
                            DropdownMenuItem(
                              value: t.id,
                              child: Text(t.name),
                            ),
                        ],
                        onChanged: (id) {
                          if (id != null) {
                            final target =
                                data.terrariums.firstWhere((t) => t.id == id);
                            _loadTerrariumThresholds(target);
                          }
                        },
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 4),
                const Text(
                  'Cài đặt khoảng an toàn để kích hoạt cảnh báo tức thời',
                  style: TextStyle(fontSize: 11, color: AppColors.textMuted),
                ),
                const SizedBox(height: 16),

                // Temp Fields
                const Text(
                  'Nhiệt độ (°C)',
                  style: TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.bold,
                    color: AppColors.statusDanger,
                  ),
                ),
                const SizedBox(height: 6),
                Row(
                  children: [
                    Expanded(
                      child: TextField(
                        controller: _tempMinCtrl,
                        keyboardType: TextInputType.number,
                        decoration: const InputDecoration(labelText: 'Min (°C)'),
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: TextField(
                        controller: _tempMaxCtrl,
                        keyboardType: TextInputType.number,
                        decoration: const InputDecoration(labelText: 'Max (°C)'),
                      ),
                    ),
                  ],
                ),

                const SizedBox(height: 14),

                // Humidity Fields
                const Text(
                  'Độ ẩm (%)',
                  style: TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.bold,
                    color: AppColors.primary,
                  ),
                ),
                const SizedBox(height: 6),
                Row(
                  children: [
                    Expanded(
                      child: TextField(
                        controller: _humMinCtrl,
                        keyboardType: TextInputType.number,
                        decoration: const InputDecoration(labelText: 'Min (%)'),
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: TextField(
                        controller: _humMaxCtrl,
                        keyboardType: TextInputType.number,
                        decoration: const InputDecoration(labelText: 'Max (%)'),
                      ),
                    ),
                  ],
                ),

                const SizedBox(height: 14),

                // Light Fields
                const Text(
                  'Ánh sáng (Lux)',
                  style: TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.bold,
                    color: AppColors.accent,
                  ),
                ),
                const SizedBox(height: 6),
                Row(
                  children: [
                    Expanded(
                      child: TextField(
                        controller: _lightMinCtrl,
                        keyboardType: TextInputType.number,
                        decoration: const InputDecoration(labelText: 'Min (Lx)'),
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: TextField(
                        controller: _lightMaxCtrl,
                        keyboardType: TextInputType.number,
                        decoration: const InputDecoration(labelText: 'Max (Lx)'),
                      ),
                    ),
                  ],
                ),

                const SizedBox(height: 18),

                SizedBox(
                  width: double.infinity,
                  height: 44,
                  child: ElevatedButton(
                    style: ElevatedButton.styleFrom(
                      backgroundColor: AppColors.primary,
                      foregroundColor: Colors.white,
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(12),
                      ),
                    ),
                    onPressed: () {
                      final updated = ThresholdModel(
                        tempMin: double.tryParse(_tempMinCtrl.text) ?? 28.0,
                        tempMax: double.tryParse(_tempMaxCtrl.text) ?? 38.0,
                        humidityMin: double.tryParse(_humMinCtrl.text) ?? 30.0,
                        humidityMax: double.tryParse(_humMaxCtrl.text) ?? 45.0,
                        lightMin: int.tryParse(_lightMinCtrl.text) ?? 500,
                        lightMax: int.tryParse(_lightMaxCtrl.text) ?? 1200,
                      );

                      data.updateThresholds(_currentTerrariumId, updated);
                      ScaffoldMessenger.of(context).showSnackBar(
                        const SnackBar(
                          content: Text('Đã cập nhật cấu hình ngưỡng an toàn!'),
                          backgroundColor: AppColors.primary,
                        ),
                      );
                    },
                    child: const Text('Lưu cấu hình ngưỡng',
                        style: TextStyle(fontWeight: FontWeight.bold)),
                  ),
                ),
              ],
            ),
          ),

          const SizedBox(height: 20),

          // 3. IoT Hardware Management (9 devices)
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
                      'Thiết bị IoT kết nối',
                      style: TextStyle(
                        fontSize: 15,
                        fontWeight: FontWeight.bold,
                        color: AppColors.textMain,
                      ),
                    ),
                    Text(
                      '${data.onlineDevicesCount}/${devices.length} Online',
                      style: const TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.bold,
                        color: AppColors.primary,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                for (final d in devices)
                  Container(
                    margin: const EdgeInsets.only(bottom: 8),
                    padding: const EdgeInsets.all(10),
                    decoration: BoxDecoration(
                      color: AppColors.bgMain,
                      borderRadius: BorderRadius.circular(12),
                      border: Border.all(color: AppColors.border),
                    ),
                    child: Row(
                      children: [
                        Icon(
                          Icons.memory,
                          size: 16,
                          color: d.isOnline
                              ? AppColors.primary
                              : AppColors.statusDanger,
                        ),
                        const SizedBox(width: 10),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                d.name,
                                style: const TextStyle(
                                  fontWeight: FontWeight.bold,
                                  fontSize: 12,
                                  color: AppColors.textMain,
                                ),
                              ),
                              Text(
                                '${d.terrariumName} • ${d.lastSeen}',
                                style: const TextStyle(
                                    fontSize: 10, color: AppColors.textMuted),
                              ),
                            ],
                          ),
                        ),
                        Container(
                          width: 8,
                          height: 8,
                          decoration: BoxDecoration(
                            shape: BoxShape.circle,
                            color: d.isOnline
                                ? AppColors.primary
                                : AppColors.statusDanger,
                          ),
                        ),
                      ],
                    ),
                  ),
              ],
            ),
          ),

          const SizedBox(height: 20),

          // 4. Notifications Toggles
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
                const Text(
                  'Kênh cảnh báo',
                  style: TextStyle(
                    fontSize: 15,
                    fontWeight: FontWeight.bold,
                    color: AppColors.textMain,
                  ),
                ),
                SwitchListTile(
                  title: const Text('Cảnh báo nhiệt độ',
                      style: TextStyle(fontSize: 13, color: AppColors.textMain)),
                  subtitle: const Text('Gửi thông báo khi nhiệt vượt ngưỡng',
                      style:
                          TextStyle(fontSize: 11, color: AppColors.textMuted)),
                  value: _notifyTemp,
                  activeThumbColor: AppColors.primary,
                  onChanged: (val) => setState(() => _notifyTemp = val),
                ),
                SwitchListTile(
                  title: const Text('Cảnh báo độ ẩm',
                      style: TextStyle(fontSize: 13, color: AppColors.textMain)),
                  subtitle: const Text('Gửi thông báo khi độ ẩm vi phạm',
                      style:
                          TextStyle(fontSize: 11, color: AppColors.textMuted)),
                  value: _notifyHumidity,
                  activeThumbColor: AppColors.primary,
                  onChanged: (val) => setState(() => _notifyHumidity = val),
                ),
                SwitchListTile(
                  title: const Text('Thiết bị ngoại tuyến',
                      style: TextStyle(fontSize: 13, color: AppColors.textMain)),
                  subtitle: const Text('Báo khi node ESP32 mất kết nối WiFi',
                      style:
                          TextStyle(fontSize: 11, color: AppColors.textMuted)),
                  value: _notifyOffline,
                  activeThumbColor: AppColors.primary,
                  onChanged: (val) => setState(() => _notifyOffline = val),
                ),
              ],
            ),
          ),

          const SizedBox(height: 20),

          // Logout
          SizedBox(
            width: double.infinity,
            height: 48,
            child: OutlinedButton.icon(
              style: OutlinedButton.styleFrom(
                foregroundColor: AppColors.statusDanger,
                side: BorderSide(
                    color: AppColors.statusDanger.withValues(alpha: 0.5)),
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(14),
                ),
              ),
              onPressed: () {
                ScaffoldMessenger.of(context).showSnackBar(
                  const SnackBar(
                    content: Text('Đã đăng xuất khỏi tài khoản quản trị.'),
                  ),
                );
              },
              icon: const Icon(Icons.logout, size: 18),
              label: const Text('Đăng xuất'),
            ),
          ),
          const SizedBox(height: 24),
        ],
      ),
    );
  }
}
