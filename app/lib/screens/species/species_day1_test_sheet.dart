import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/species_profile.dart';
import '../../state/session_provider.dart';
import '../../state/species_profile_provider.dart';
import '../../theme/app_theme.dart';

/// Modal bottom sheet providing UI testing actions for Day 1 Species Profile Data Layer.
class SpeciesDay1TestSheet extends StatefulWidget {
  const SpeciesDay1TestSheet({super.key});

  static void show(BuildContext context) {
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (_) => const SpeciesDay1TestSheet(),
    );
  }

  @override
  State<SpeciesDay1TestSheet> createState() => _SpeciesDay1TestSheetState();
}

class _SpeciesDay1TestSheetState extends State<SpeciesDay1TestSheet> {
  String? _lastActionLog;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      final session = context.read<SessionProvider>();
      if (session.isSignedIn) {
        context.read<SpeciesProfileProvider>().fetchProfiles();
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final session = context.watch<SessionProvider>();
    final provider = context.watch<SpeciesProfileProvider>();

    return Container(
      height: MediaQuery.of(context).size.height * 0.85,
      decoration: const BoxDecoration(
        color: AppColors.bgCard,
        borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
      ),
      child: Column(
        children: [
          // Drag handle
          Container(
            margin: const EdgeInsets.only(top: 12, bottom: 8),
            width: 40,
            height: 4,
            decoration: BoxDecoration(
              color: Colors.white24,
              borderRadius: BorderRadius.circular(2),
            ),
          ),

          // Header
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 8),
            child: Row(
              children: [
                Container(
                  padding: const EdgeInsets.all(8),
                  decoration: BoxDecoration(
                    color: AppColors.primary.withValues(alpha: 0.15),
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: const Icon(Icons.bug_report, color: AppColors.primary, size: 22),
                ),
                const SizedBox(width: 12),
                const Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'Test Chức Năng Ngày 1 (Hồ sơ loài)',
                        style: TextStyle(
                          fontSize: 16,
                          fontWeight: FontWeight.bold,
                          color: AppColors.textMain,
                        ),
                      ),
                      Text(
                        'Kiểm tra kết nối trực tiếp API Backend',
                        style: TextStyle(fontSize: 12, color: AppColors.textMuted),
                      ),
                    ],
                  ),
                ),
                IconButton(
                  icon: const Icon(Icons.close, color: AppColors.textMuted),
                  onPressed: () => Navigator.pop(context),
                ),
              ],
            ),
          ),

          const Divider(color: AppColors.border, height: 1),

          // Sign in warning if not signed in
          if (!session.isSignedIn)
            Container(
              margin: const EdgeInsets.all(16),
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: Colors.amber.withValues(alpha: 0.1),
                border: Border.all(color: Colors.amber.withValues(alpha: 0.3)),
                borderRadius: BorderRadius.circular(12),
              ),
              child: const Row(
                children: [
                  Icon(Icons.warning_amber_rounded, color: Colors.amber, size: 20),
                  SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      'Bạn chưa đăng nhập! Hãy đăng nhập ở thẻ trên màn hình Cài đặt để có token gọi API.',
                      style: TextStyle(fontSize: 12, color: Colors.amber),
                    ),
                  ),
                ],
              ),
            ),

          // Action buttons bar
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: Column(
              children: [
                Row(
                  children: [
                    Expanded(
                      child: ElevatedButton.icon(
                        style: ElevatedButton.styleFrom(
                          backgroundColor: AppColors.primary,
                          foregroundColor: Colors.black,
                          padding: const EdgeInsets.symmetric(vertical: 10),
                        ),
                        onPressed: provider.isLoading || provider.isSaving
                            ? null
                            : () async {
                                await provider.fetchProfiles();
                                setState(() {
                                  _lastActionLog = 'Đã gọi GET /api/v1/species-profiles. Tải về ${provider.profiles.length} loài.';
                                });
                              },
                        icon: const Icon(Icons.refresh, size: 16),
                        label: const Text('1. Tải loài (GET)', style: TextStyle(fontSize: 12)),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: OutlinedButton.icon(
                        style: OutlinedButton.styleFrom(
                          foregroundColor: AppColors.primary,
                          padding: const EdgeInsets.symmetric(vertical: 10),
                        ),
                        onPressed: provider.isLoading || provider.isSaving
                            ? null
                            : () async {
                                final timestamp = DateTime.now().millisecondsSinceEpoch.toString().substring(8);
                                final created = await provider.createProfile(
                                  CreateSpeciesProfileRequest(
                                    name: 'Tắc kè Test $timestamp',
                                    scientificName: 'Gekko gecko $timestamp',
                                    climateZone: ClimateZones.tropical,
                                    notes: 'Hồ sơ tạo tự động từ Test Dialog Ngày 1.',
                                    bands: const [
                                      SpeciesProfileBandInput(
                                        metric: 'tempC',
                                        phase: 'day',
                                        targetMin: 28.0,
                                        targetMax: 32.0,
                                      ),
                                      SpeciesProfileBandInput(
                                        metric: 'humidity',
                                        phase: 'any',
                                        targetMin: 60.0,
                                        targetMax: 80.0,
                                      ),
                                    ],
                                  ),
                                );
                                setState(() {
                                  if (created != null) {
                                    _lastActionLog = 'Thành công: Đã tạo "${created.name}" (ID: ${created.id.substring(0, 8)}...)';
                                  } else {
                                    _lastActionLog = 'Lỗi tạo loài: ${provider.errorMessage}';
                                  }
                                });
                              },
                        icon: const Icon(Icons.add, size: 16),
                        label: const Text('2. Thêm thử (POST)', style: TextStyle(fontSize: 12)),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                Row(
                  children: [
                    Expanded(
                      child: OutlinedButton.icon(
                        style: OutlinedButton.styleFrom(
                          foregroundColor: Colors.orangeAccent,
                          padding: const EdgeInsets.symmetric(vertical: 10),
                        ),
                        onPressed: provider.isLoading || provider.isSaving || provider.profiles.isEmpty
                            ? null
                            : () async {
                                final target = provider.profiles.first;
                                final duplicated = await provider.duplicateProfile(
                                  target.id,
                                  name: '${target.name} (Bản sao test)',
                                );
                                setState(() {
                                  if (duplicated != null) {
                                    _lastActionLog = 'Thành công nhân bản: "${duplicated.name}"';
                                  } else {
                                    _lastActionLog = 'Lỗi nhân bản: ${provider.errorMessage}';
                                  }
                                });
                              },
                        icon: const Icon(Icons.copy, size: 16),
                        label: const Text('3. Nhân bản', style: TextStyle(fontSize: 12)),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: OutlinedButton.icon(
                        style: OutlinedButton.styleFrom(
                          foregroundColor: Colors.redAccent,
                          padding: const EdgeInsets.symmetric(vertical: 10),
                        ),
                        onPressed: provider.isLoading || provider.isSaving || provider.builtInProfiles.isEmpty
                            ? null
                            : () async {
                                final builtIn = provider.builtInProfiles.first;
                                await provider.deleteProfile(builtIn.id);
                                setState(() {
                                  _lastActionLog = 'Kết quả test xóa loài mặc định: ${provider.errorMessage}';
                                });
                              },
                        icon: const Icon(Icons.security, size: 16),
                        label: const Text('4. Test xóa 403', style: TextStyle(fontSize: 12)),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),

          // Status & log banner
          if (_lastActionLog != null || provider.errorMessage != null)
            Container(
              margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
              padding: const EdgeInsets.all(10),
              decoration: BoxDecoration(
                color: (provider.errorMessage != null)
                    ? Colors.red.withValues(alpha: 0.15)
                    : AppColors.primary.withValues(alpha: 0.1),
                border: Border.all(
                  color: (provider.errorMessage != null)
                      ? Colors.red.withValues(alpha: 0.3)
                      : AppColors.primary.withValues(alpha: 0.3),
                ),
                borderRadius: BorderRadius.circular(10),
              ),
              child: Text(
                provider.errorMessage ?? _lastActionLog ?? '',
                style: TextStyle(
                  fontSize: 12,
                  color: provider.errorMessage != null ? Colors.redAccent : AppColors.primary,
                ),
              ),
            ),

          const SizedBox(height: 8),

          // List title
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            child: Row(
              children: [
                Text(
                  'Danh sách loài tải từ API (${provider.profiles.length})',
                  style: const TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.bold,
                    color: AppColors.textMain,
                  ),
                ),
                const Spacer(),
                if (provider.isLoading || provider.isSaving)
                  const SizedBox(
                    width: 14,
                    height: 14,
                    child: CircularProgressIndicator(strokeWidth: 2, color: AppColors.primary),
                  ),
              ],
            ),
          ),

          const SizedBox(height: 6),

          // Profiles list
          Expanded(
            child: provider.profiles.isEmpty
                ? Center(
                    child: provider.isLoading
                        ? const CircularProgressIndicator(color: AppColors.primary)
                        : const Text(
                            'Chưa có dữ liệu.\nNhấn nút "1. Tải loài (GET)" ở trên để gọi API.',
                            textAlign: TextAlign.center,
                            style: TextStyle(fontSize: 13, color: AppColors.textMuted),
                          ),
                  )
                : ListView.builder(
                    padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                    itemCount: provider.profiles.length,
                    itemBuilder: (context, index) {
                      final item = provider.profiles[index];
                      return Card(
                        color: AppColors.bgCard,
                        margin: const EdgeInsets.only(bottom: 8),
                        shape: RoundedRectangleBorder(
                          side: BorderSide(
                            color: item.isBuiltIn ? Colors.white12 : AppColors.primary.withValues(alpha: 0.3),
                          ),
                          borderRadius: BorderRadius.circular(12),
                        ),
                        child: Padding(
                          padding: const EdgeInsets.all(12),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Row(
                                children: [
                                  Expanded(
                                    child: Text(
                                      item.name,
                                      style: const TextStyle(
                                        fontSize: 14,
                                        fontWeight: FontWeight.bold,
                                        color: AppColors.textMain,
                                      ),
                                    ),
                                  ),
                                  Container(
                                    padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                                    decoration: BoxDecoration(
                                      color: item.isBuiltIn ? Colors.white10 : AppColors.primary.withValues(alpha: 0.2),
                                      borderRadius: BorderRadius.circular(6),
                                    ),
                                    child: Text(
                                      item.isBuiltIn ? 'Hệ thống' : 'Tùy chỉnh',
                                      style: TextStyle(
                                        fontSize: 10,
                                        color: item.isBuiltIn ? AppColors.textMuted : AppColors.primary,
                                      ),
                                    ),
                                  ),
                                  if (!item.isBuiltIn) ...[
                                    const SizedBox(width: 6),
                                    InkWell(
                                      onTap: () async {
                                        await provider.deleteProfile(item.id);
                                        setState(() {
                                          _lastActionLog = 'Đã xóa loài "${item.name}"';
                                        });
                                      },
                                      child: const Icon(Icons.delete_outline, color: Colors.redAccent, size: 18),
                                    ),
                                  ],
                                ],
                              ),
                              if (item.scientificName.isNotEmpty) ...[
                                const SizedBox(height: 2),
                                Text(
                                  item.scientificName,
                                  style: const TextStyle(
                                    fontSize: 12,
                                    fontStyle: FontStyle.italic,
                                    color: AppColors.textMuted,
                                  ),
                                ),
                              ],
                              const SizedBox(height: 6),
                              Row(
                                children: [
                                  Text(
                                    'Vùng: ${ClimateZones.labelOf(item.climateZone)}',
                                    style: const TextStyle(fontSize: 11, color: AppColors.textMuted),
                                  ),
                                  const SizedBox(width: 12),
                                  Text(
                                    'Ngưỡng: ${item.bands.length} dải',
                                    style: const TextStyle(fontSize: 11, color: AppColors.textMuted),
                                  ),
                                ],
                              ),
                            ],
                          ),
                        ),
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }
}
