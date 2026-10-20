import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/species_profile.dart';
import '../../state/session_provider.dart';
import '../../state/species_profile_provider.dart';
import '../../theme/app_theme.dart';

/// Bottom sheet for managing Reptile Species Profiles (Browse, Create with Form, Duplicate, Edit, Delete).
class SpeciesProfileSheet extends StatefulWidget {
  const SpeciesProfileSheet({super.key});

  static void show(BuildContext context) {
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (_) => const SpeciesProfileSheet(),
    );
  }

  @override
  State<SpeciesProfileSheet> createState() => _SpeciesProfileSheetState();
}

class _SpeciesProfileSheetState extends State<SpeciesProfileSheet> {
  String? _statusMessage;
  bool _isSuccess = true;

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

  void _showNotification(String message, {bool isSuccess = true}) {
    setState(() {
      _statusMessage = message;
      _isSuccess = isSuccess;
    });
  }

  // ================= DIALOG 1: TẠO MỚI / CHỈNH SỬA HỒ SƠ =================
  Future<void> _openSpeciesFormDialog({SpeciesProfileView? existing}) async {
    final isEditing = existing != null;
    final nameCtrl = TextEditingController(text: existing?.name ?? '');
    final sciNameCtrl = TextEditingController(
      text: existing?.scientificName ?? '',
    );
    final notesCtrl = TextEditingController(text: existing?.notes ?? '');
    final photoperiodCtrl = TextEditingController(
      text: (existing?.photoperiodHours ?? 12.0).toStringAsFixed(0),
    );

    // Tìm dải nhiệt độ và độ ẩm hiện có nếu đang sửa
    double tempMin = 28.0;
    double tempMax = 33.0;
    double humMin = 50.0;
    double humMax = 75.0;

    if (isEditing) {
      for (final band in existing.bands) {
        if (band.metric == 'tempC') {
          tempMin = band.targetMin;
          tempMax = band.targetMax;
        } else if (band.metric == 'humidityPct' || band.metric == 'humidity') {
          humMin = band.targetMin;
          humMax = band.targetMax;
        }
      }
    }

    final tempMinCtrl = TextEditingController(text: tempMin.toStringAsFixed(1));
    final tempMaxCtrl = TextEditingController(text: tempMax.toStringAsFixed(1));
    final humMinCtrl = TextEditingController(text: humMin.toStringAsFixed(0));
    final humMaxCtrl = TextEditingController(text: humMax.toStringAsFixed(0));

    String selectedZone = existing?.climateZone ?? ClimateZones.tropical;
    final formKey = GlobalKey<FormState>();

    await showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (ctx) {
        return StatefulBuilder(
          builder: (modalCtx, setModalState) {
            return Container(
              padding: EdgeInsets.only(
                bottom: MediaQuery.of(modalCtx).viewInsets.bottom + 20,
                top: 16,
                left: 20,
                right: 20,
              ),
              constraints: BoxConstraints(
                maxHeight: MediaQuery.of(modalCtx).size.height * 0.9,
              ),
              decoration: const BoxDecoration(
                color: AppColors.bgCard,
                borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
              ),
              child: Form(
                key: formKey,
                child: ListView(
                  shrinkWrap: true,
                  children: [
                    // Handle
                    Center(
                      child: Container(
                        width: 40,
                        height: 4,
                        decoration: BoxDecoration(
                          color: Colors.white24,
                          borderRadius: BorderRadius.circular(2),
                        ),
                      ),
                    ),
                    const SizedBox(height: 14),

                    // Header
                    Row(
                      children: [
                        Icon(
                          isEditing ? Icons.edit : Icons.add_circle_outline,
                          color: AppColors.primary,
                        ),
                        const SizedBox(width: 8),
                        Text(
                          isEditing
                              ? 'Tùy chỉnh hồ sơ loài'
                              : 'Thêm hồ sơ loài mới',
                          style: const TextStyle(
                            fontSize: 17,
                            fontWeight: FontWeight.bold,
                            color: AppColors.textMain,
                          ),
                        ),
                        const Spacer(),
                        IconButton(
                          icon: const Icon(
                            Icons.close,
                            color: AppColors.textMuted,
                          ),
                          onPressed: () => Navigator.pop(modalCtx),
                        ),
                      ],
                    ),
                    const Divider(color: AppColors.border),
                    const SizedBox(height: 10),

                    // Tên loài
                    TextFormField(
                      controller: nameCtrl,
                      decoration: const InputDecoration(
                        labelText: 'Tên loài bò sát *',
                        hintText: 'VD: Rồng râu Úc, Tắc kè hoa Yemen...',
                        prefixIcon: Icon(Icons.pets, size: 20),
                      ),
                      validator: (val) {
                        if (val == null || val.trim().isEmpty) {
                          return 'Vui lòng nhập tên loài';
                        }
                        return null;
                      },
                    ),
                    const SizedBox(height: 12),

                    // Tên khoa học
                    TextFormField(
                      controller: sciNameCtrl,
                      decoration: const InputDecoration(
                        labelText: 'Tên khoa học (Tùy chọn)',
                        hintText: 'VD: Pogona vitticeps',
                        prefixIcon: Icon(Icons.biotech, size: 20),
                      ),
                    ),
                    const SizedBox(height: 12),

                    // Vùng khí hậu
                    DropdownButtonFormField<String>(
                      initialValue: selectedZone,
                      decoration: const InputDecoration(
                        labelText: 'Vùng khí hậu tự nhiên *',
                        prefixIcon: Icon(Icons.public, size: 20),
                      ),
                      dropdownColor: AppColors.bgInput,
                      items: ClimateZones.all.map((zone) {
                        return DropdownMenuItem(
                          value: zone,
                          child: Text(ClimateZones.labelOf(zone)),
                        );
                      }).toList(),
                      onChanged: (val) {
                        if (val != null) {
                          setModalState(() => selectedZone = val);
                        }
                      },
                    ),
                    const SizedBox(height: 16),

                    const Text(
                      'Tiêu chuẩn nhiệt độ & độ ẩm vi khí hậu:',
                      style: TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.bold,
                        color: AppColors.textMain,
                      ),
                    ),
                    const SizedBox(height: 8),

                    // Nhiệt độ min - max
                    Row(
                      children: [
                        Expanded(
                          child: TextFormField(
                            controller: tempMinCtrl,
                            keyboardType: const TextInputType.numberWithOptions(
                              decimal: true,
                            ),
                            decoration: const InputDecoration(
                              labelText: 'Nhiệt độ min (°C)',
                              prefixIcon: Icon(Icons.thermostat, size: 18),
                            ),
                            validator: (val) =>
                                val == null || double.tryParse(val) == null
                                ? 'Nhập số hợp lệ'
                                : null,
                          ),
                        ),
                        const SizedBox(width: 10),
                        Expanded(
                          child: TextFormField(
                            controller: tempMaxCtrl,
                            keyboardType: const TextInputType.numberWithOptions(
                              decimal: true,
                            ),
                            decoration: const InputDecoration(
                              labelText: 'Nhiệt độ max (°C)',
                              prefixIcon: Icon(Icons.thermostat, size: 18),
                            ),
                            validator: (val) {
                              final min = double.tryParse(tempMinCtrl.text);
                              final max = double.tryParse(val ?? '');
                              if (max == null) return 'Nhập số hợp lệ';
                              if (min != null && max <= min) return 'Max > Min';
                              return null;
                            },
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 12),

                    // Độ ẩm min - max
                    Row(
                      children: [
                        Expanded(
                          child: TextFormField(
                            controller: humMinCtrl,
                            keyboardType: TextInputType.number,
                            decoration: const InputDecoration(
                              labelText: 'Độ ẩm min (%)',
                              prefixIcon: Icon(Icons.water_drop, size: 18),
                            ),
                            validator: (val) =>
                                val == null || double.tryParse(val) == null
                                ? 'Nhập số hợp lệ'
                                : null,
                          ),
                        ),
                        const SizedBox(width: 10),
                        Expanded(
                          child: TextFormField(
                            controller: humMaxCtrl,
                            keyboardType: TextInputType.number,
                            decoration: const InputDecoration(
                              labelText: 'Độ ẩm max (%)',
                              prefixIcon: Icon(Icons.water_drop, size: 18),
                            ),
                            validator: (val) {
                              final min = double.tryParse(humMinCtrl.text);
                              final max = double.tryParse(val ?? '');
                              if (max == null) return 'Nhập số hợp lệ';
                              if (min != null && max <= min) return 'Max > Min';
                              return null;
                            },
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 12),

                    // Chiếu sáng
                    TextFormField(
                      controller: photoperiodCtrl,
                      keyboardType: TextInputType.number,
                      decoration: const InputDecoration(
                        labelText: 'Thời gian chiếu sáng mỗi ngày (giờ)',
                        prefixIcon: Icon(Icons.wb_sunny_outlined, size: 18),
                        hintText: 'VD: 12',
                      ),
                      validator: (val) {
                        final hours = double.tryParse(val ?? '');
                        if (hours == null || hours <= 0 || hours > 24) {
                          return 'Từ 1 đến 24 giờ';
                        }
                        return null;
                      },
                    ),
                    const SizedBox(height: 12),

                    // Ghi chú
                    TextFormField(
                      controller: notesCtrl,
                      maxLines: 2,
                      decoration: const InputDecoration(
                        labelText: 'Ghi chú tập tính & chăm sóc',
                        hintText:
                            'VD: Cần đèn sưởi basking spot, bổ sung canxi...',
                        prefixIcon: Icon(Icons.notes, size: 18),
                      ),
                    ),
                    const SizedBox(height: 20),

                    // Nút Submit
                    ElevatedButton(
                      style: ElevatedButton.styleFrom(
                        backgroundColor: AppColors.primary,
                        foregroundColor: Colors.black,
                        padding: const EdgeInsets.symmetric(vertical: 14),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(12),
                        ),
                      ),
                      onPressed: () async {
                        if (!formKey.currentState!.validate()) return;

                        final tMin = double.parse(tempMinCtrl.text);
                        final tMax = double.parse(tempMaxCtrl.text);
                        final hMin = double.parse(humMinCtrl.text);
                        final hMax = double.parse(humMaxCtrl.text);
                        final photoHours = double.parse(photoperiodCtrl.text);

                        final bands = [
                          SpeciesProfileBandInput(
                            metric: 'tempC',
                            phase: 'day',
                            targetMin: tMin,
                            targetMax: tMax,
                          ),
                          SpeciesProfileBandInput(
                            metric: 'humidityPct',
                            phase: 'any',
                            targetMin: hMin,
                            targetMax: hMax,
                          ),
                        ];

                        final provider = context.read<SpeciesProfileProvider>();
                        Navigator.pop(modalCtx);

                        if (isEditing) {
                          final updated = await provider.updateProfile(
                            existing.id,
                            UpdateSpeciesProfileRequest(
                              name: nameCtrl.text.trim(),
                              scientificName: sciNameCtrl.text.trim(),
                              climateZone: selectedZone,
                              photoperiodHours: photoHours,
                              notes: notesCtrl.text.trim(),
                              bands: bands,
                            ),
                          );
                          if (updated != null) {
                            _showNotification(
                              'Đã cập nhật hồ sơ loài "${updated.name}"',
                            );
                          } else {
                            _showNotification(
                              provider.errorMessage ??
                                  'Không thể cập nhật hồ sơ',
                              isSuccess: false,
                            );
                          }
                        } else {
                          final created = await provider.createProfile(
                            CreateSpeciesProfileRequest(
                              name: nameCtrl.text.trim(),
                              scientificName: sciNameCtrl.text.trim(),
                              climateZone: selectedZone,
                              photoperiodHours: photoHours,
                              notes: notesCtrl.text.trim(),
                              bands: bands,
                            ),
                          );
                          if (created != null) {
                            _showNotification(
                              'Đã tạo thành công loài "${created.name}"',
                            );
                          } else {
                            _showNotification(
                              provider.errorMessage ??
                                  'Không thể tạo hồ sơ mới',
                              isSuccess: false,
                            );
                          }
                        }
                      },
                      child: Text(
                        isEditing ? 'Lưu thay đổi' : 'Tạo hồ sơ loài',
                        style: const TextStyle(
                          fontWeight: FontWeight.bold,
                          fontSize: 15,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            );
          },
        );
      },
    );
  }

  // ================= DIALOG 2: NHÂN BẢN LOÀI =================
  Future<void> _openDuplicateDialog(SpeciesProfileView target) async {
    final provider = context.read<SpeciesProfileProvider>();
    final nameCtrl = TextEditingController(text: '${target.name} (Tùy chỉnh)');
    final formKey = GlobalKey<FormState>();

    final confirm = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        backgroundColor: AppColors.bgCard,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
        title: Row(
          children: [
            const Icon(Icons.copy, color: Colors.orangeAccent, size: 20),
            const SizedBox(width: 8),
            const Expanded(
              child: Text(
                'Nhân bản hồ sơ loài',
                style: TextStyle(
                  color: AppColors.textMain,
                  fontSize: 16,
                  fontWeight: FontWeight.bold,
                ),
              ),
            ),
          ],
        ),
        content: Form(
          key: formKey,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'Tạo một bản sao từ "${target.name}" để tự do tinh chỉnh các thông số vi khí hậu.',
                style: const TextStyle(
                  color: AppColors.textMuted,
                  fontSize: 13,
                ),
              ),
              const SizedBox(height: 14),
              TextFormField(
                controller: nameCtrl,
                decoration: const InputDecoration(
                  labelText: 'Tên bản sao mới *',
                  hintText: 'Nhập tên hồ sơ mới...',
                ),
                validator: (val) {
                  if (val == null || val.trim().isEmpty) {
                    return 'Vui lòng nhập tên cho bản sao';
                  }
                  return null;
                },
              ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text(
              'Hủy',
              style: TextStyle(color: AppColors.textMuted),
            ),
          ),
          ElevatedButton(
            style: ElevatedButton.styleFrom(
              backgroundColor: Colors.orangeAccent,
              foregroundColor: Colors.black,
            ),
            onPressed: () {
              if (formKey.currentState!.validate()) {
                Navigator.pop(ctx, true);
              }
            },
            child: const Text('Xác nhận nhân bản'),
          ),
        ],
      ),
    );

    if (confirm == true) {
      final duplicated = await provider.duplicateProfile(
        target.id,
        name: nameCtrl.text.trim(),
      );
      if (!mounted) return;
      if (duplicated != null) {
        _showNotification('Đã nhân bản thành công: "${duplicated.name}"');
      } else {
        _showNotification(
          provider.errorMessage ?? 'Không thể nhân bản hồ sơ loài',
          isSuccess: false,
        );
      }
    }
  }

  // ================= DIALOG 3: XEM CHI TIẾT LOÀI MẶC ĐỊNH =================
  void _showBuiltInInfoDialog(SpeciesProfileView item) {
    showDialog(
      context: context,
      builder: (ctx) => AlertDialog(
        backgroundColor: AppColors.bgCard,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
        title: Row(
          children: [
            const Icon(Icons.lock_outline, color: AppColors.primary, size: 20),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                item.name,
                style: const TextStyle(
                  color: AppColors.textMain,
                  fontSize: 16,
                  fontWeight: FontWeight.bold,
                ),
              ),
            ),
          ],
        ),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (item.scientificName.isNotEmpty) ...[
              Text(
                item.scientificName,
                style: const TextStyle(
                  fontStyle: FontStyle.italic,
                  color: AppColors.textMuted,
                  fontSize: 13,
                ),
              ),
              const SizedBox(height: 10),
            ],
            Container(
              padding: const EdgeInsets.all(10),
              decoration: BoxDecoration(
                color: Colors.white.withValues(alpha: 0.05),
                borderRadius: BorderRadius.circular(10),
              ),
              child: const Text(
                'Đây là hồ sơ loài mặc định của hệ thống (Read-only) nhằm đảm bảo chuẩn đối sánh. Bạn có thể nhấn "Nhân bản" để tạo bản sao tùy chỉnh theo nhu cầu riêng.',
                style: TextStyle(fontSize: 12, color: AppColors.textMuted),
              ),
            ),
            const SizedBox(height: 12),
            Text(
              '• Vùng khí hậu: ${ClimateZones.labelOf(item.climateZone)}',
              style: const TextStyle(fontSize: 12, color: AppColors.textMain),
            ),
            Text(
              '• Thời gian chiếu sáng: ${item.photoperiodHours.toStringAsFixed(0)} giờ/ngày',
              style: const TextStyle(fontSize: 12, color: AppColors.textMain),
            ),
            Text(
              '• Số dải cảm biến: ${item.bands.length} dải',
              style: const TextStyle(fontSize: 12, color: AppColors.textMain),
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text(
              'Đóng',
              style: TextStyle(color: AppColors.textMuted),
            ),
          ),
          ElevatedButton.icon(
            style: ElevatedButton.styleFrom(
              backgroundColor: Colors.orangeAccent,
              foregroundColor: Colors.black,
            ),
            icon: const Icon(Icons.copy, size: 16),
            label: const Text('Nhân bản để sửa'),
            onPressed: () {
              Navigator.pop(ctx);
              _openDuplicateDialog(item);
            },
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final session = context.watch<SessionProvider>();
    final provider = context.watch<SpeciesProfileProvider>();

    return Container(
      height: MediaQuery.of(context).size.height * 0.90,
      decoration: const BoxDecoration(
        color: AppColors.bgCard,
        borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
      ),
      child: Column(
        children: [
          // Drag handle
          Container(
            margin: const EdgeInsets.only(top: 12, bottom: 8),
            width: 44,
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
                  padding: const EdgeInsets.all(10),
                  decoration: BoxDecoration(
                    color: AppColors.primary.withValues(alpha: 0.15),
                    borderRadius: BorderRadius.circular(12),
                  ),
                  child: const Icon(
                    Icons.pets,
                    color: AppColors.primary,
                    size: 24,
                  ),
                ),
                const SizedBox(width: 14),
                const Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'Hồ sơ loài & Vi khí hậu',
                        style: TextStyle(
                          fontSize: 17,
                          fontWeight: FontWeight.bold,
                          color: AppColors.textMain,
                        ),
                      ),
                      SizedBox(height: 2),
                      Text(
                        'Danh mục loài và thiết lập thông số môi trường chuẩn',
                        style: TextStyle(
                          fontSize: 12,
                          color: AppColors.textMuted,
                        ),
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
                  Icon(Icons.info_outline, color: Colors.amber, size: 20),
                  SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      'Vui lòng đăng nhập từ màn hình Cài đặt để quản lý và chỉnh sửa hồ sơ loài.',
                      style: TextStyle(fontSize: 12, color: Colors.amber),
                    ),
                  ),
                ],
              ),
            ),

          // Action toolbar
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: Row(
              children: [
                // 1. Refresh button
                OutlinedButton.icon(
                  style: OutlinedButton.styleFrom(
                    foregroundColor: AppColors.textMain,
                    side: const BorderSide(color: AppColors.border),
                    padding: const EdgeInsets.symmetric(
                      vertical: 11,
                      horizontal: 14,
                    ),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(12),
                    ),
                  ),
                  onPressed: provider.isLoading || provider.isSaving
                      ? null
                      : () async {
                          await provider.fetchProfiles();
                          _showNotification(
                            'Đã làm mới danh mục (${provider.profiles.length} loài).',
                          );
                        },
                  icon: const Icon(Icons.refresh, size: 16),
                  label: const Text(
                    'Làm mới',
                    style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
                  ),
                ),
                const SizedBox(width: 10),

                // 2. Add profile button (Opens Form)
                Expanded(
                  child: ElevatedButton.icon(
                    style: ElevatedButton.styleFrom(
                      backgroundColor: AppColors.primary,
                      foregroundColor: Colors.black,
                      elevation: 0,
                      padding: const EdgeInsets.symmetric(vertical: 11),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(12),
                      ),
                    ),
                    onPressed: provider.isLoading || provider.isSaving
                        ? null
                        : () => _openSpeciesFormDialog(),
                    icon: const Icon(Icons.add, size: 18),
                    label: const Text(
                      'Thêm hồ sơ loài',
                      style: TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),

          // Status & log banner
          if (_statusMessage != null || provider.errorMessage != null)
            Container(
              margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 9),
              decoration: BoxDecoration(
                color: (_isSuccess && provider.errorMessage == null)
                    ? AppColors.primary.withValues(alpha: 0.12)
                    : Colors.redAccent.withValues(alpha: 0.12),
                border: Border.all(
                  color: (_isSuccess && provider.errorMessage == null)
                      ? AppColors.primary.withValues(alpha: 0.3)
                      : Colors.redAccent.withValues(alpha: 0.3),
                ),
                borderRadius: BorderRadius.circular(10),
              ),
              child: Row(
                children: [
                  Icon(
                    (_isSuccess && provider.errorMessage == null)
                        ? Icons.check_circle_outline
                        : Icons.error_outline,
                    size: 16,
                    color: (_isSuccess && provider.errorMessage == null)
                        ? AppColors.primary
                        : Colors.redAccent,
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      provider.errorMessage ?? _statusMessage ?? '',
                      style: TextStyle(
                        fontSize: 12,
                        color: (_isSuccess && provider.errorMessage == null)
                            ? AppColors.primary
                            : Colors.redAccent,
                      ),
                    ),
                  ),
                ],
              ),
            ),

          const SizedBox(height: 6),

          // List title bar
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
            child: Row(
              children: [
                const Text(
                  'Danh mục hồ sơ hiện có',
                  style: TextStyle(
                    fontSize: 14,
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
                    color: Colors.white10,
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: Text(
                    '${provider.profiles.length}',
                    style: const TextStyle(
                      fontSize: 11,
                      fontWeight: FontWeight.bold,
                      color: AppColors.primary,
                    ),
                  ),
                ),
                const Spacer(),
                if (provider.isLoading || provider.isSaving)
                  const SizedBox(
                    width: 14,
                    height: 14,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: AppColors.primary,
                    ),
                  ),
              ],
            ),
          ),

          const SizedBox(height: 4),

          // Profiles list
          Expanded(
            child: provider.profiles.isEmpty
                ? Center(
                    child: provider.isLoading
                        ? const CircularProgressIndicator(
                            color: AppColors.primary,
                          )
                        : const Column(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              Icon(Icons.pets, size: 40, color: Colors.white24),
                              SizedBox(height: 8),
                              Text(
                                'Chưa có hồ sơ loài nào trong danh mục.\nNhấn nút "Thêm hồ sơ loài" ở trên để điền thông tin.',
                                textAlign: TextAlign.center,
                                style: TextStyle(
                                  fontSize: 13,
                                  color: AppColors.textMuted,
                                ),
                              ),
                            ],
                          ),
                  )
                : ListView.builder(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 16,
                      vertical: 6,
                    ),
                    itemCount: provider.profiles.length,
                    itemBuilder: (context, index) {
                      final item = provider.profiles[index];
                      return Card(
                        color: AppColors.bgCard,
                        margin: const EdgeInsets.only(bottom: 10),
                        elevation: 0,
                        shape: RoundedRectangleBorder(
                          side: BorderSide(
                            color: item.isBuiltIn
                                ? AppColors.border
                                : AppColors.primary.withValues(alpha: 0.35),
                          ),
                          borderRadius: BorderRadius.circular(14),
                        ),
                        child: Padding(
                          padding: const EdgeInsets.all(12),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              // Hàng tiêu đề & badges
                              Row(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  // Icon đại diện
                                  Container(
                                    width: 38,
                                    height: 38,
                                    decoration: BoxDecoration(
                                      color: item.isBuiltIn
                                          ? Colors.white10
                                          : AppColors.primary.withValues(
                                              alpha: 0.15,
                                            ),
                                      borderRadius: BorderRadius.circular(10),
                                    ),
                                    child: Icon(
                                      Icons.pets,
                                      size: 18,
                                      color: item.isBuiltIn
                                          ? AppColors.textMuted
                                          : AppColors.primary,
                                    ),
                                  ),
                                  const SizedBox(width: 12),

                                  // Tên & tên khoa học
                                  Expanded(
                                    child: Column(
                                      crossAxisAlignment:
                                          CrossAxisAlignment.start,
                                      children: [
                                        Text(
                                          item.name,
                                          style: const TextStyle(
                                            fontSize: 14,
                                            fontWeight: FontWeight.bold,
                                            color: AppColors.textMain,
                                          ),
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
                                      ],
                                    ),
                                  ),

                                  // Badge loại hồ sơ
                                  Container(
                                    padding: const EdgeInsets.symmetric(
                                      horizontal: 7,
                                      vertical: 2,
                                    ),
                                    decoration: BoxDecoration(
                                      color: item.isBuiltIn
                                          ? Colors.white10
                                          : AppColors.primary.withValues(
                                              alpha: 0.2,
                                            ),
                                      borderRadius: BorderRadius.circular(6),
                                    ),
                                    child: Text(
                                      item.isBuiltIn ? 'Mặc định' : 'Tùy chỉnh',
                                      style: TextStyle(
                                        fontSize: 10,
                                        fontWeight: FontWeight.w600,
                                        color: item.isBuiltIn
                                            ? AppColors.textMuted
                                            : AppColors.primary,
                                      ),
                                    ),
                                  ),
                                ],
                              ),

                              const SizedBox(height: 8),

                              // Thông số môi trường
                              Wrap(
                                spacing: 12,
                                runSpacing: 4,
                                children: [
                                  Row(
                                    mainAxisSize: MainAxisSize.min,
                                    children: [
                                      const Icon(
                                        Icons.public,
                                        size: 12,
                                        color: AppColors.textMuted,
                                      ),
                                      const SizedBox(width: 4),
                                      Text(
                                        ClimateZones.labelOf(item.climateZone),
                                        style: const TextStyle(
                                          fontSize: 11,
                                          color: AppColors.textMuted,
                                        ),
                                      ),
                                    ],
                                  ),
                                  Row(
                                    mainAxisSize: MainAxisSize.min,
                                    children: [
                                      const Icon(
                                        Icons.tune,
                                        size: 12,
                                        color: AppColors.textMuted,
                                      ),
                                      const SizedBox(width: 4),
                                      Text(
                                        '${item.bands.length} dải ngưỡng',
                                        style: const TextStyle(
                                          fontSize: 11,
                                          color: AppColors.textMuted,
                                        ),
                                      ),
                                    ],
                                  ),
                                  Row(
                                    mainAxisSize: MainAxisSize.min,
                                    children: [
                                      const Icon(
                                        Icons.wb_sunny_outlined,
                                        size: 12,
                                        color: AppColors.textMuted,
                                      ),
                                      const SizedBox(width: 4),
                                      Text(
                                        '${item.photoperiodHours.toStringAsFixed(0)}h sáng',
                                        style: const TextStyle(
                                          fontSize: 11,
                                          color: AppColors.textMuted,
                                        ),
                                      ),
                                    ],
                                  ),
                                ],
                              ),

                              const Divider(
                                color: AppColors.border,
                                height: 16,
                              ),

                              // Hàng nút thao tác theo từng thẻ (Nhân bản, Tùy chỉnh, Xóa)
                              Row(
                                mainAxisAlignment: MainAxisAlignment.end,
                                children: [
                                  // Nút Nhân bản (Cho phép nhân bản chính xác loài này)
                                  TextButton.icon(
                                    style: TextButton.styleFrom(
                                      foregroundColor: Colors.orangeAccent,
                                      padding: const EdgeInsets.symmetric(
                                        horizontal: 8,
                                        vertical: 4,
                                      ),
                                      minimumSize: Size.zero,
                                      tapTargetSize:
                                          MaterialTapTargetSize.shrinkWrap,
                                    ),
                                    icon: const Icon(Icons.copy, size: 14),
                                    label: const Text(
                                      'Nhân bản',
                                      style: TextStyle(fontSize: 12),
                                    ),
                                    onPressed: () => _openDuplicateDialog(item),
                                  ),
                                  const SizedBox(width: 8),

                                  // Nếu là hồ sơ tùy chỉnh: Có nút "Sửa" và "Xóa"
                                  if (!item.isBuiltIn) ...[
                                    TextButton.icon(
                                      style: TextButton.styleFrom(
                                        foregroundColor: AppColors.primary,
                                        padding: const EdgeInsets.symmetric(
                                          horizontal: 8,
                                          vertical: 4,
                                        ),
                                        minimumSize: Size.zero,
                                        tapTargetSize:
                                            MaterialTapTargetSize.shrinkWrap,
                                      ),
                                      icon: const Icon(Icons.edit, size: 14),
                                      label: const Text(
                                        'Tùy chỉnh',
                                        style: TextStyle(fontSize: 12),
                                      ),
                                      onPressed: () => _openSpeciesFormDialog(
                                        existing: item,
                                      ),
                                    ),
                                    const SizedBox(width: 8),
                                    IconButton(
                                      icon: const Icon(
                                        Icons.delete_outline,
                                        color: Colors.redAccent,
                                        size: 18,
                                      ),
                                      padding: EdgeInsets.zero,
                                      constraints: const BoxConstraints(),
                                      tooltip: 'Xóa hồ sơ',
                                      onPressed: () async {
                                        final confirm = await showDialog<bool>(
                                          context: context,
                                          builder: (ctx) => AlertDialog(
                                            backgroundColor: AppColors.bgCard,
                                            title: const Text(
                                              'Xác nhận xóa',
                                              style: TextStyle(
                                                color: AppColors.textMain,
                                                fontSize: 16,
                                              ),
                                            ),
                                            content: Text(
                                              'Bạn có chắc muốn xóa hồ sơ loài "${item.name}" không?',
                                              style: const TextStyle(
                                                color: AppColors.textMuted,
                                                fontSize: 13,
                                              ),
                                            ),
                                            actions: [
                                              TextButton(
                                                onPressed: () =>
                                                    Navigator.pop(ctx, false),
                                                child: const Text(
                                                  'Hủy',
                                                  style: TextStyle(
                                                    color: AppColors.textMuted,
                                                  ),
                                                ),
                                              ),
                                              TextButton(
                                                onPressed: () =>
                                                    Navigator.pop(ctx, true),
                                                child: const Text(
                                                  'Xóa',
                                                  style: TextStyle(
                                                    color: Colors.redAccent,
                                                  ),
                                                ),
                                              ),
                                            ],
                                          ),
                                        );
                                        if (confirm == true) {
                                          final success = await provider
                                              .deleteProfile(item.id);
                                          if (success) {
                                            _showNotification(
                                              'Đã xóa loài "${item.name}"',
                                            );
                                          } else {
                                            _showNotification(
                                              provider.errorMessage ??
                                                  'Không thể xóa loài',
                                              isSuccess: false,
                                            );
                                          }
                                        }
                                      },
                                    ),
                                  ] else ...[
                                    // Hồ sơ hệ thống mặc định: Nút xem chi tiết / thông tin bảo vệ
                                    TextButton.icon(
                                      style: TextButton.styleFrom(
                                        foregroundColor: AppColors.textMuted,
                                        padding: const EdgeInsets.symmetric(
                                          horizontal: 8,
                                          vertical: 4,
                                        ),
                                        minimumSize: Size.zero,
                                        tapTargetSize:
                                            MaterialTapTargetSize.shrinkWrap,
                                      ),
                                      icon: const Icon(
                                        Icons.info_outline,
                                        size: 14,
                                      ),
                                      label: const Text(
                                        'Chi tiết',
                                        style: TextStyle(fontSize: 12),
                                      ),
                                      onPressed: () =>
                                          _showBuiltInInfoDialog(item),
                                    ),
                                  ],
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
