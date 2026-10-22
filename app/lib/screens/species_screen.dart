import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../core/permissions.dart';
import '../models/species_profile.dart';
import '../state/session_provider.dart';
import '../state/species_profile_provider.dart';
import '../theme/app_theme.dart';

/// Full screen for managing & browsing Reptile Species Profiles.
/// Strictly enforces Admin vs Regular User capabilities based on `species.write` permission.
class SpeciesScreen extends StatefulWidget {
  const SpeciesScreen({super.key});

  @override
  State<SpeciesScreen> createState() => _SpeciesScreenState();
}

class _SpeciesScreenState extends State<SpeciesScreen> {
  String? _statusMessage;
  bool _isSuccess = true;
  String _searchQuery = '';
  String? _selectedClimateFilter;

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

  // ================= FORM DIALOG: TẠO MỚI / CHỈNH SỬA =================
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

  // ================= DIALOG: NHÂN BẢN LOÀI =================
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
                'Tạo một bản sao từ "${target.name}" để tinh chỉnh thông số vi khí hậu.',
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

  // ================= DIALOG: XEM CHI TIẾT LOÀI =================
  void _showDetailDialog(SpeciesProfileView item, bool canManage) {
    showDialog(
      context: context,
      builder: (ctx) => AlertDialog(
        backgroundColor: AppColors.bgCard,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
        title: Row(
          children: [
            Icon(
              item.isBuiltIn ? Icons.lock_outline : Icons.pets,
              color: AppColors.primary,
              size: 20,
            ),
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
        content: SingleChildScrollView(
          child: Column(
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
                child: Text(
                  item.isBuiltIn
                      ? 'Đây là hồ sơ loài mặc định hệ thống. Các bể nuôi có thể sử dụng thông số này làm chuẩn đối sánh.'
                      : 'Hồ sơ loài tùy chỉnh do người dùng tạo.',
                  style: const TextStyle(
                    fontSize: 12,
                    color: AppColors.textMuted,
                  ),
                ),
              ),
              const SizedBox(height: 12),
              Text(
                '• Vùng khí hậu: ${ClimateZones.labelOf(item.climateZone)}',
                style: const TextStyle(fontSize: 13, color: AppColors.textMain),
              ),
              const SizedBox(height: 4),
              Text(
                '• Chu kỳ chiếu sáng: ${item.photoperiodHours.toStringAsFixed(0)} giờ/ngày (Bật đèn lúc ${item.lightsOnLocalTime})',
                style: const TextStyle(fontSize: 13, color: AppColors.textMain),
              ),
              const SizedBox(height: 4),
              Text(
                '• Cường độ sáng chuẩn: ${item.lightThresholdLux.toStringAsFixed(0)} Lux',
                style: const TextStyle(fontSize: 13, color: AppColors.textMain),
              ),
              const SizedBox(height: 12),
              const Text(
                'Ngưỡng vi khí hậu chi tiết:',
                style: TextStyle(
                  fontSize: 13,
                  fontWeight: FontWeight.bold,
                  color: AppColors.primary,
                ),
              ),
              const SizedBox(height: 6),
              for (final band in item.bands)
                Padding(
                  padding: const EdgeInsets.only(bottom: 4),
                  child: Text(
                    '  - ${band.metric == 'tempC'
                        ? 'Nhiệt độ'
                        : band.metric == 'humidityPct'
                        ? 'Độ ẩm'
                        : band.metric}: ${band.targetMin.toStringAsFixed(1)} ~ ${band.targetMax.toStringAsFixed(1)} ${band.unit} (${band.phase})',
                    style: const TextStyle(
                      fontSize: 12,
                      color: AppColors.textMain,
                    ),
                  ),
                ),
              if (item.notes != null && item.notes!.isNotEmpty) ...[
                const SizedBox(height: 10),
                Text(
                  'Ghi chú: ${item.notes}',
                  style: const TextStyle(
                    fontSize: 12,
                    color: AppColors.textMuted,
                  ),
                ),
              ],
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text(
              'Đóng',
              style: TextStyle(color: AppColors.textMuted),
            ),
          ),
          if (canManage)
            ElevatedButton.icon(
              style: ElevatedButton.styleFrom(
                backgroundColor: Colors.orangeAccent,
                foregroundColor: Colors.black,
              ),
              icon: const Icon(Icons.copy, size: 16),
              label: const Text('Nhân bản'),
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

    // Logic kiểm tra vai trò Admin vs User:
    // Admin có quyền `species.write` hoặc role là Admin/Administrator -> toàn quyền thêm/sửa/xóa/nhân bản.
    // User thường chỉ có `species.read` -> chỉ tra cứu, xem thông số.
    final isAdmin = (session.user?.role.toLowerCase() == 'administrator') ||
        (session.user?.role.toLowerCase() == 'admin') ||
        session.hasPermission(Permissions.dataReadAny) ||
        session.hasPermission(Permissions.userRead);

    final canManage =
        session.hasPermission(Permissions.speciesWrite) || isAdmin;

    // Filter profiles theo query và climate zone
    final filtered = provider.profiles.where((p) {
      if (_selectedClimateFilter != null &&
          p.climateZone != _selectedClimateFilter) {
        return false;
      }
      if (_searchQuery.isNotEmpty) {
        final q = _searchQuery.toLowerCase();
        return p.name.toLowerCase().contains(q) ||
            p.scientificName.toLowerCase().contains(q);
      }
      return true;
    }).toList();

    return Scaffold(
      backgroundColor: AppColors.bgMain,
      body: SafeArea(
        child: Column(
          children: [
            // Top Bar & Role guide banner (hidden for role admin, displayed for other roles)
            if (!isAdmin) ...[
              // Top Bar: Tiêu đề và badge vai trò
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
                child: Row(
                  children: [
                    Container(
                      padding: const EdgeInsets.all(9),
                      decoration: BoxDecoration(
                        color: AppColors.primary.withValues(alpha: 0.15),
                        borderRadius: BorderRadius.circular(10),
                      ),
                      child: const Icon(
                        Icons.pets,
                        color: AppColors.primary,
                        size: 22,
                      ),
                    ),
                    const SizedBox(width: 12),
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
                            'Danh mục sinh học & thông số môi trường chuẩn',
                            style: TextStyle(
                              fontSize: 12,
                              color: AppColors.textMuted,
                            ),
                          ),
                        ],
                      ),
                    ),
                    // Badge Role Admin vs User
                    Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 9,
                        vertical: 4,
                      ),
                      decoration: BoxDecoration(
                        color: canManage
                            ? AppColors.primary.withValues(alpha: 0.2)
                            : Colors.white10,
                        borderRadius: BorderRadius.circular(8),
                        border: Border.all(
                          color: canManage
                              ? AppColors.primary.withValues(alpha: 0.5)
                              : Colors.white24,
                        ),
                      ),
                      child: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Icon(
                            canManage ? Icons.shield : Icons.person_outline,
                            size: 13,
                            color: canManage
                                ? AppColors.primary
                                : AppColors.textMuted,
                          ),
                          const SizedBox(width: 4),
                          Text(
                            canManage ? 'Admin' : 'User',
                            style: TextStyle(
                              fontSize: 11,
                              fontWeight: FontWeight.bold,
                              color: canManage
                                  ? AppColors.primary
                                  : AppColors.textMuted,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),

              const Divider(color: AppColors.border, height: 1),

              // Role guide banner
              Container(
                margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                decoration: BoxDecoration(
                  color: canManage
                      ? AppColors.primary.withValues(alpha: 0.08)
                      : Colors.white.withValues(alpha: 0.04),
                  borderRadius: BorderRadius.circular(10),
                  border: Border.all(
                    color: canManage
                        ? AppColors.primary.withValues(alpha: 0.25)
                        : Colors.white12,
                  ),
                ),
                child: Row(
                  children: [
                    Icon(
                      canManage
                          ? Icons.admin_panel_settings_outlined
                          : Icons.info_outline,
                      size: 16,
                      color: canManage ? AppColors.primary : AppColors.textMuted,
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        canManage
                            ? 'Chế độ Quản trị: Bạn có toàn quyền thêm loài mới, nhân bản và tùy chỉnh danh mục.'
                            : 'Chế độ Người dùng: Tra cứu thông số vi khí hậu chuẩn để áp dụng cho bể nuôi. Chỉ Quản trị viên mới có quyền cập nhật danh mục.',
                        style: TextStyle(
                          fontSize: 11.5,
                          color: canManage
                              ? AppColors.textMain
                              : AppColors.textMuted,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ],

            if (isAdmin)
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 6),
                child: Column(
                  children: [
                    // Row 1: Tổng hồ sơ loài + Thêm loài (theo format của ảnh thứ 3)
                    Row(
                      children: [
                        Expanded(
                          child: Container(
                            padding: const EdgeInsets.symmetric(
                              horizontal: 14,
                              vertical: 12,
                            ),
                            decoration: BoxDecoration(
                              color: AppColors.bgCard,
                              borderRadius: BorderRadius.circular(14),
                              border: Border.all(color: AppColors.border),
                            ),
                            child: Row(
                              mainAxisAlignment: MainAxisAlignment.spaceBetween,
                              children: [
                                const Text(
                                  'Tổng hồ sơ loài:',
                                  style: TextStyle(
                                    color: AppColors.textMuted,
                                    fontSize: 13,
                                  ),
                                ),
                                Text(
                                  '${provider.profiles.length}',
                                  style: const TextStyle(
                                    color: AppColors.textMain,
                                    fontWeight: FontWeight.bold,
                                    fontSize: 15,
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ),
                        const SizedBox(width: 10),
                        ElevatedButton.icon(
                          style: ElevatedButton.styleFrom(
                            backgroundColor: AppColors.primary,
                            foregroundColor: Colors.white,
                            padding: const EdgeInsets.symmetric(
                              horizontal: 14,
                              vertical: 12,
                            ),
                            shape: RoundedRectangleBorder(
                              borderRadius: BorderRadius.circular(14),
                            ),
                          ),
                          icon: const Icon(Icons.add, size: 18),
                          label: const Text(
                            'Thêm loài',
                            style: TextStyle(
                              fontSize: 13,
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                          onPressed: provider.isLoading || provider.isSaving
                              ? null
                              : () => _openSpeciesFormDialog(),
                        ),
                      ],
                    ),
                    const SizedBox(height: 12),

                    // Row 2: Search bar full width (theo format của ảnh thứ 3, không có nút làm mới)
                    TextField(
                      decoration: InputDecoration(
                        hintText: 'Tìm theo tên loài, tên khoa học...',
                        hintStyle: const TextStyle(
                          fontSize: 13,
                          color: AppColors.textMuted,
                        ),
                        prefixIcon: const Icon(
                          Icons.search,
                          size: 20,
                          color: AppColors.textMuted,
                        ),
                        suffixIcon: _searchQuery.isNotEmpty
                            ? IconButton(
                                icon: const Icon(
                                  Icons.clear,
                                  size: 18,
                                  color: AppColors.textMuted,
                                ),
                                onPressed: () =>
                                    setState(() => _searchQuery = ''),
                              )
                            : null,
                        contentPadding: const EdgeInsets.symmetric(
                          vertical: 10,
                          horizontal: 12,
                        ),
                        filled: true,
                        fillColor: AppColors.bgInput,
                        border: OutlineInputBorder(
                          borderRadius: BorderRadius.circular(14),
                          borderSide: const BorderSide(
                            color: AppColors.border,
                          ),
                        ),
                        enabledBorder: OutlineInputBorder(
                          borderRadius: BorderRadius.circular(14),
                          borderSide: const BorderSide(
                            color: AppColors.border,
                          ),
                        ),
                        focusedBorder: OutlineInputBorder(
                          borderRadius: BorderRadius.circular(14),
                          borderSide: const BorderSide(
                            color: AppColors.primary,
                          ),
                        ),
                      ),
                      onChanged: (val) =>
                          setState(() => _searchQuery = val.trim()),
                    ),
                  ],
                ),
              )
            else
              // Action toolbar (Search + Add if Admin + Refresh) for other roles
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
                child: Row(
                  children: [
                    // Ô tìm kiếm nhanh
                    Expanded(
                      child: SizedBox(
                        height: 40,
                        child: TextField(
                          decoration: InputDecoration(
                            hintText: 'Tìm loài...',
                            hintStyle: const TextStyle(
                              fontSize: 12,
                              color: AppColors.textMuted,
                            ),
                            prefixIcon: const Icon(
                              Icons.search,
                              size: 18,
                              color: AppColors.textMuted,
                            ),
                            contentPadding: const EdgeInsets.symmetric(
                              vertical: 0,
                              horizontal: 12,
                            ),
                            filled: true,
                            fillColor: AppColors.bgInput,
                            border: OutlineInputBorder(
                              borderRadius: BorderRadius.circular(10),
                              borderSide: const BorderSide(
                                color: AppColors.border,
                              ),
                            ),
                            enabledBorder: OutlineInputBorder(
                              borderRadius: BorderRadius.circular(10),
                              borderSide: const BorderSide(
                                color: AppColors.border,
                              ),
                            ),
                          ),
                          onChanged: (val) =>
                              setState(() => _searchQuery = val.trim()),
                        ),
                      ),
                    ),
                    const SizedBox(width: 8),

                    // Nút làm mới
                    IconButton(
                      style: IconButton.styleFrom(
                        backgroundColor: AppColors.bgInput,
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(10),
                          side: const BorderSide(color: AppColors.border),
                        ),
                      ),
                      icon: const Icon(
                        Icons.refresh,
                        size: 18,
                        color: AppColors.textMain,
                      ),
                      tooltip: 'Làm mới',
                      onPressed: provider.isLoading || provider.isSaving
                          ? null
                          : () async {
                              await provider.fetchProfiles();
                              _showNotification(
                                'Đã làm mới danh mục (${provider.profiles.length} loài).',
                              );
                            },
                    ),

                    // Nút "Thêm hồ sơ loài" (CHỈ HIỆN VỚI ADMIN)
                    if (canManage) ...[
                      const SizedBox(width: 8),
                      ElevatedButton.icon(
                        style: ElevatedButton.styleFrom(
                          backgroundColor: AppColors.primary,
                          foregroundColor: Colors.black,
                          padding: const EdgeInsets.symmetric(
                            horizontal: 12,
                            vertical: 10,
                          ),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(10),
                          ),
                        ),
                        icon: const Icon(Icons.add, size: 18),
                        label: const Text(
                          'Thêm loài',
                          style: TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.bold,
                          ),
                        ),
                        onPressed: provider.isLoading || provider.isSaving
                            ? null
                            : () => _openSpeciesFormDialog(),
                      ),
                    ],
                  ],
                ),
              ),

            // Climate zone filter chips
            SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
              child: Row(
                children: [
                  ChoiceChip(
                    label: const Text('Tất cả', style: TextStyle(fontSize: 11)),
                    selected: _selectedClimateFilter == null,
                    onSelected: (_) =>
                        setState(() => _selectedClimateFilter = null),
                  ),
                  const SizedBox(width: 6),
                  for (final zone in ClimateZones.all) ...[
                    ChoiceChip(
                      label: Text(
                        ClimateZones.labelOf(zone),
                        style: const TextStyle(fontSize: 11),
                      ),
                      selected: _selectedClimateFilter == zone,
                      onSelected: (sel) => setState(
                        () => _selectedClimateFilter = sel ? zone : null,
                      ),
                    ),
                    const SizedBox(width: 6),
                  ],
                ],
              ),
            ),

            // Status message banner
            if (_statusMessage != null || provider.errorMessage != null)
              Container(
                margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
                padding: const EdgeInsets.symmetric(
                  horizontal: 14,
                  vertical: 8,
                ),
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

            // Danh sách hồ sơ loài
            Expanded(
              child: filtered.isEmpty
                  ? Center(
                      child: provider.isLoading
                          ? const CircularProgressIndicator(
                              color: AppColors.primary,
                            )
                          : Column(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                const Icon(
                                  Icons.pets,
                                  size: 40,
                                  color: Colors.white24,
                                ),
                                const SizedBox(height: 8),
                                Text(
                                  canManage
                                      ? 'Chưa có hồ sơ loài nào khớp bộ lọc.\nNhấn nút "Thêm loài" để tạo mới.'
                                      : 'Không tìm thấy hồ sơ loài nào phù hợp.',
                                  textAlign: TextAlign.center,
                                  style: const TextStyle(
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
                      itemCount: filtered.length,
                      itemBuilder: (context, index) {
                        final item = filtered[index];
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
                          child: InkWell(
                            borderRadius: BorderRadius.circular(14),
                            onTap: () => _showDetailDialog(item, canManage),
                            child: Padding(
                              padding: const EdgeInsets.all(12),
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Row(
                                    crossAxisAlignment:
                                        CrossAxisAlignment.start,
                                    children: [
                                      Container(
                                        width: 38,
                                        height: 38,
                                        decoration: BoxDecoration(
                                          color: item.isBuiltIn
                                              ? Colors.white10
                                              : AppColors.primary.withValues(
                                                  alpha: 0.15,
                                                ),
                                          borderRadius: BorderRadius.circular(
                                            10,
                                          ),
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
                                            if (item
                                                .scientificName
                                                .isNotEmpty) ...[
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
                                          borderRadius: BorderRadius.circular(
                                            6,
                                          ),
                                        ),
                                        child: Text(
                                          item.isBuiltIn
                                              ? 'Mặc định'
                                              : 'Tùy chỉnh',
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
                                            ClimateZones.labelOf(
                                              item.climateZone,
                                            ),
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

                                  Row(
                                    mainAxisAlignment: MainAxisAlignment.end,
                                    children: [
                                      // Nút xem chi tiết (Cả Admin và User đều dùng được)
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
                                          Icons.visibility_outlined,
                                          size: 14,
                                        ),
                                        label: const Text(
                                          'Chi tiết',
                                          style: TextStyle(fontSize: 12),
                                        ),
                                        onPressed: () =>
                                            _showDetailDialog(item, canManage),
                                      ),

                                      // CÁC THAO TÁC GHI: CHỈ DÀNH CHO ADMIN
                                      if (canManage) ...[
                                        const SizedBox(width: 6),
                                        TextButton.icon(
                                          style: TextButton.styleFrom(
                                            foregroundColor:
                                                Colors.orangeAccent,
                                            padding: const EdgeInsets.symmetric(
                                              horizontal: 8,
                                              vertical: 4,
                                            ),
                                            minimumSize: Size.zero,
                                            tapTargetSize: MaterialTapTargetSize
                                                .shrinkWrap,
                                          ),
                                          icon: const Icon(
                                            Icons.copy,
                                            size: 14,
                                          ),
                                          label: const Text(
                                            'Nhân bản',
                                            style: TextStyle(fontSize: 12),
                                          ),
                                          onPressed: () =>
                                              _openDuplicateDialog(item),
                                        ),

                                        if (!item.isBuiltIn) ...[
                                          const SizedBox(width: 6),
                                          TextButton.icon(
                                            style: TextButton.styleFrom(
                                              foregroundColor:
                                                  AppColors.primary,
                                              padding:
                                                  const EdgeInsets.symmetric(
                                                    horizontal: 8,
                                                    vertical: 4,
                                                  ),
                                              minimumSize: Size.zero,
                                              tapTargetSize:
                                                  MaterialTapTargetSize
                                                      .shrinkWrap,
                                            ),
                                            icon: const Icon(
                                              Icons.edit,
                                              size: 14,
                                            ),
                                            label: const Text(
                                              'Tùy chỉnh',
                                              style: TextStyle(fontSize: 12),
                                            ),
                                            onPressed: () =>
                                                _openSpeciesFormDialog(
                                                  existing: item,
                                                ),
                                          ),
                                          const SizedBox(width: 6),
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
                                                  backgroundColor:
                                                      AppColors.bgCard,
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
                                                      color:
                                                          AppColors.textMuted,
                                                      fontSize: 13,
                                                    ),
                                                  ),
                                                  actions: [
                                                    TextButton(
                                                      onPressed: () =>
                                                          Navigator.pop(
                                                            ctx,
                                                            false,
                                                          ),
                                                      child: const Text(
                                                        'Hủy',
                                                        style: TextStyle(
                                                          color: AppColors
                                                              .textMuted,
                                                        ),
                                                      ),
                                                    ),
                                                    TextButton(
                                                      onPressed: () =>
                                                          Navigator.pop(
                                                            ctx,
                                                            true,
                                                          ),
                                                      child: const Text(
                                                        'Xóa',
                                                        style: TextStyle(
                                                          color:
                                                              Colors.redAccent,
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
                                        ],
                                      ],
                                    ],
                                  ),
                                ],
                              ),
                            ),
                          ),
                        );
                      },
                    ),
            ),
          ],
        ),
      ),
    );
  }
}
