import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';

import '../core/permissions.dart';
import '../l10n/generated/app_localizations.dart';
import '../models/device_model.dart';
import '../state/device_provider.dart';
import '../state/session_provider.dart';
import '../state/terrarium_provider.dart';
import '../theme/app_theme.dart';

/// Screen managing IoT devices fleet (matching Web `/devices` functionality).
class DevicesScreen extends StatefulWidget {
  const DevicesScreen({super.key});

  @override
  State<DevicesScreen> createState() => _DevicesScreenState();
}

class _DevicesScreenState extends State<DevicesScreen> {
  final _searchController = TextEditingController();

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        context.read<DeviceProvider>().loadDevices();
        context.read<TerrariumProvider>().loadTerrariums();
      }
    });
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final session = context.watch<SessionProvider>();
    final deviceProv = context.watch<DeviceProvider>();
    final canWrite = session.hasPermission(Permissions.deviceWrite);
    final canProvision = session.hasPermission(Permissions.deviceProvision);
    final isAdmin = (session.user?.role.toLowerCase() == 'administrator') ||
        (session.user?.role.toLowerCase() == 'admin') ||
        session.hasPermission(Permissions.dataReadAny) ||
        session.hasPermission(Permissions.userRead);

    return Scaffold(
      backgroundColor: AppColors.bgMain,
      appBar: isAdmin
          ? null
          : AppBar(
              centerTitle: true,
              title: Text(l10n.navDevices),
              actions: [
                if (canProvision)
                  IconButton(
                    tooltip: 'Nhận thiết bị',
                    icon: const Icon(Icons.add_link),
                    onPressed: () => _showClaimDialog(context),
                  ),
                IconButton(
                  tooltip: 'Làm mới',
                  icon: const Icon(Icons.refresh),
                  onPressed: () => deviceProv.loadDevices(),
                ),
              ],
            ),
      body: RefreshIndicator(
        color: AppColors.primary,
        onRefresh: () async {
          await deviceProv.loadDevices();
          if (context.mounted) {
            await context.read<TerrariumProvider>().loadTerrariums();
          }
        },
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            // Summary row
            Row(
              children: [
                Expanded(
                  child: Container(
                    padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                    decoration: BoxDecoration(
                      color: AppColors.bgCard,
                      borderRadius: BorderRadius.circular(14),
                      border: Border.all(color: AppColors.border),
                    ),
                    child: Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        const Text(
                          'Tổng thiết bị:',
                          style: TextStyle(color: AppColors.textMuted, fontSize: 13),
                        ),
                        Text(
                          '${deviceProv.devices.length}',
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
                if (canProvision) ...[
                  const SizedBox(width: 10),
                  ElevatedButton.icon(
                    style: ElevatedButton.styleFrom(
                      backgroundColor: AppColors.primary,
                      foregroundColor: Colors.white,
                      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(14),
                      ),
                    ),
                    onPressed: () => _showClaimDialog(context),
                    icon: const Icon(Icons.add, size: 18),
                    label: const Text('Nhận thiết bị', style: TextStyle(fontSize: 13)),
                  ),
                ],
              ],
            ),

            // Offline alert banner if any
            if (deviceProv.offlineCount > 0) ...[
              const SizedBox(height: 12),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: AppColors.statusWarning.withValues(alpha: 0.12),
                  borderRadius: BorderRadius.circular(14),
                  border: Border.all(
                    color: AppColors.statusWarning.withValues(alpha: 0.35),
                  ),
                ),
                child: Row(
                  children: [
                    const Icon(
                      Icons.warning_amber_rounded,
                      color: AppColors.statusWarning,
                      size: 20,
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Text(
                        '${deviceProv.offlineCount} thiết bị đang ngoại tuyến',
                        style: const TextStyle(
                          color: AppColors.textMain,
                          fontWeight: FontWeight.w600,
                          fontSize: 13,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ],

            const SizedBox(height: 14),

            // Search bar
            TextField(
              controller: _searchController,
              style: const TextStyle(color: AppColors.textMain, fontSize: 13),
              decoration: InputDecoration(
                hintText: 'Tìm theo mã, tên, MAC hoặc bể nuôi...',
                hintStyle: const TextStyle(color: AppColors.textMuted, fontSize: 13),
                prefixIcon: const Icon(Icons.search, color: AppColors.textMuted, size: 20),
                suffixIcon: _searchController.text.isNotEmpty
                    ? IconButton(
                        icon: const Icon(Icons.clear, size: 18, color: AppColors.textMuted),
                        onPressed: () {
                          _searchController.clear();
                          deviceProv.setSearchQuery('');
                        },
                      )
                    : null,
                filled: true,
                fillColor: AppColors.bgInput,
                contentPadding: const EdgeInsets.symmetric(vertical: 10),
                border: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(14),
                  borderSide: const BorderSide(color: AppColors.border),
                ),
                enabledBorder: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(14),
                  borderSide: const BorderSide(color: AppColors.border),
                ),
                focusedBorder: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(14),
                  borderSide: const BorderSide(color: AppColors.primary),
                ),
              ),
              onChanged: (val) => deviceProv.setSearchQuery(val),
            ),

            const SizedBox(height: 10),

            // Filter chips
            SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: Row(
                children: [
                  _FilterChip(
                    label: 'Tất cả',
                    selected: deviceProv.statusFilter == 'all',
                    onTap: () => deviceProv.setStatusFilter('all'),
                  ),
                  const SizedBox(width: 8),
                  _FilterChip(
                    label: 'Trực tuyến',
                    selected: deviceProv.statusFilter == 'online',
                    onTap: () => deviceProv.setStatusFilter('online'),
                  ),
                  const SizedBox(width: 8),
                  _FilterChip(
                    label: 'Ngoại tuyến',
                    selected: deviceProv.statusFilter == 'offline',
                    onTap: () => deviceProv.setStatusFilter('offline'),
                  ),
                ],
              ),
            ),

            const SizedBox(height: 14),

            // Device list content
            if (deviceProv.isLoading && deviceProv.devices.isEmpty)
              const Center(
                child: Padding(
                  padding: EdgeInsets.all(40),
                  child: CircularProgressIndicator(color: AppColors.primary),
                ),
              )
            else if (deviceProv.lastFailure != null && deviceProv.devices.isEmpty)
              _ErrorState(
                failure: deviceProv.lastFailure!,
                onRetry: () => deviceProv.loadDevices(),
              )
            else if (deviceProv.filteredDevices.isEmpty)
              _EmptyState(
                isSearch: deviceProv.searchQuery.isNotEmpty ||
                    deviceProv.statusFilter != 'all',
                onClaimTap: canProvision ? () => _showClaimDialog(context) : null,
              )
            else
              for (final device in deviceProv.filteredDevices) ...[
                _DeviceCard(
                  device: device,
                  canWrite: canWrite,
                  canProvision: canProvision,
                  onEdit: () => _showEditDialog(context, device),
                  onRotateSecret: () => _rotateSecret(context, device),
                  onRevoke: () => _revokeDevice(context, device),
                  onDelete: () => _deleteDevice(context, device),
                  onTap: () => _showDetailDialog(context, device),
                ),
                const SizedBox(height: 12),
              ],
          ],
        ),
      ),
    );
  }

  void _showClaimDialog(BuildContext context) {
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: AppColors.bgCard,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      builder: (_) => const _ClaimDeviceSheet(),
    );
  }

  void _showEditDialog(BuildContext context, DeviceItem device) {
    showDialog(
      context: context,
      builder: (_) => _EditDeviceDialog(device: device),
    );
  }

  void _showDetailDialog(BuildContext context, DeviceItem device) {
    showDialog(
      context: context,
      builder: (_) => _DeviceDetailDialog(device: device),
    );
  }

  Future<void> _rotateSecret(BuildContext context, DeviceItem device) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        backgroundColor: AppColors.bgCard,
        title: const Text('Đổi khóa thiết bị', style: TextStyle(color: AppColors.textMain)),
        content: Text(
          'Tạo khóa bí mật mới cho ${device.deviceName}? Khóa cũ sẽ hết hiệu lực sau 10 phút.',
          style: const TextStyle(color: AppColors.textMuted, fontSize: 13),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('Huỷ', style: TextStyle(color: AppColors.textMuted)),
          ),
          ElevatedButton(
            style: ElevatedButton.styleFrom(
              backgroundColor: AppColors.primary,
              foregroundColor: Colors.white,
            ),
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('Đổi khóa'),
          ),
        ],
      ),
    );

    if (confirmed != true || !context.mounted) return;

    try {
      final secret = await context.read<DeviceProvider>().rotateSecret(device.deviceId);
      if (context.mounted) {
        _showSecretDialog(context, secret, device.deviceName);
      }
    } catch (e) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Lỗi đổi khóa: $e')),
        );
      }
    }
  }

  Future<void> _revokeDevice(BuildContext context, DeviceItem device) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        backgroundColor: AppColors.bgCard,
        title: const Text('Thu hồi khóa', style: TextStyle(color: AppColors.statusWarning)),
        content: Text(
          'Thu hồi khóa của thiết bị "${device.deviceName}"? Thiết bị sẽ dừng gửi dữ liệu cho tới khi được đổi khóa mới.',
          style: const TextStyle(color: AppColors.textMuted, fontSize: 13),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('Huỷ', style: TextStyle(color: AppColors.textMuted)),
          ),
          ElevatedButton(
            style: ElevatedButton.styleFrom(
              backgroundColor: AppColors.statusWarning,
              foregroundColor: Colors.white,
            ),
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('Thu hồi'),
          ),
        ],
      ),
    );

    if (confirmed != true || !context.mounted) return;

    try {
      await context.read<DeviceProvider>().revokeDevice(device.deviceId);
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Đã thu hồi khóa thiết bị.')),
        );
      }
    } catch (e) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Lỗi thu hồi khóa: $e')),
        );
      }
    }
  }

  Future<void> _deleteDevice(BuildContext context, DeviceItem device) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        backgroundColor: AppColors.bgCard,
        title: const Text('Gỡ thiết bị', style: TextStyle(color: AppColors.statusDanger)),
        content: Text(
          'Gỡ thiết bị "${device.deviceName}" khỏi bể nuôi và tài khoản? Thiết bị sẽ bị huỷ liên kết để có thể gán cho bể khác.',
          style: const TextStyle(color: AppColors.textMuted, fontSize: 13),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('Huỷ', style: TextStyle(color: AppColors.textMuted)),
          ),
          ElevatedButton(
            style: ElevatedButton.styleFrom(
              backgroundColor: AppColors.statusDanger,
              foregroundColor: Colors.white,
            ),
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('Gỡ thiết bị'),
          ),
        ],
      ),
    );

    if (confirmed != true || !context.mounted) return;

    try {
      await context.read<DeviceProvider>().deleteDevice(device.deviceId);
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Đã gỡ thiết bị thành công.')),
        );
      }
    } catch (e) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Lỗi gỡ thiết bị: $e')),
        );
      }
    }
  }

  static void _showSecretDialog(BuildContext context, String secret, String deviceName) {
    showDialog(
      context: context,
      barrierDismissible: false,
      builder: (ctx) => AlertDialog(
        backgroundColor: AppColors.bgCard,
        title: const Row(
          children: [
            Icon(Icons.key, color: AppColors.accent, size: 22),
            SizedBox(width: 8),
            Text('Khóa bí mật thiết bị', style: TextStyle(color: AppColors.textMain, fontSize: 16)),
          ],
        ),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Khóa này chỉ hiển thị một lần duy nhất. Hãy sao chép và cấu hình vào bo mạch:',
              style: TextStyle(color: AppColors.textMuted, fontSize: 13),
            ),
            const SizedBox(height: 12),
            Container(
              width: double.infinity,
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: AppColors.bgInput,
                borderRadius: BorderRadius.circular(10),
                border: Border.all(color: AppColors.accent.withValues(alpha: 0.5)),
              ),
              child: SelectableText(
                secret,
                style: const TextStyle(
                  fontFamily: 'monospace',
                  color: AppColors.accent,
                  fontSize: 14,
                  fontWeight: FontWeight.bold,
                ),
              ),
            ),
          ],
        ),
        actions: [
          TextButton.icon(
            onPressed: () {
              Clipboard.setData(ClipboardData(text: secret));
              ScaffoldMessenger.of(ctx).showSnackBar(
                const SnackBar(content: Text('Đã sao chép khóa bí mật vào bộ nhớ tạm.')),
              );
            },
            icon: const Icon(Icons.copy, size: 16),
            label: const Text('Sao chép'),
          ),
          ElevatedButton(
            style: ElevatedButton.styleFrom(
              backgroundColor: AppColors.primary,
              foregroundColor: Colors.white,
            ),
            onPressed: () => Navigator.pop(ctx),
            child: const Text('Đã lưu khóa'),
          ),
        ],
      ),
    );
  }
}

class _FilterChip extends StatelessWidget {
  const _FilterChip({
    required this.label,
    required this.selected,
    required this.onTap,
  });

  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(12),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 7),
        decoration: BoxDecoration(
          color: selected ? AppColors.primary.withValues(alpha: 0.2) : AppColors.bgCard,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(
            color: selected ? AppColors.primary : AppColors.border,
          ),
        ),
        child: Text(
          label,
          style: TextStyle(
            fontSize: 12,
            fontWeight: selected ? FontWeight.bold : FontWeight.normal,
            color: selected ? AppColors.primary : AppColors.textMuted,
          ),
        ),
      ),
    );
  }
}

class _DeviceCard extends StatelessWidget {
  const _DeviceCard({
    required this.device,
    required this.canWrite,
    required this.canProvision,
    required this.onEdit,
    required this.onRotateSecret,
    required this.onRevoke,
    required this.onDelete,
    required this.onTap,
  });

  final DeviceItem device;
  final bool canWrite;
  final bool canProvision;
  final VoidCallback onEdit;
  final VoidCallback onRotateSecret;
  final VoidCallback onRevoke;
  final VoidCallback onDelete;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final isOnline = device.isOnline;

    return Container(
      decoration: BoxDecoration(
        color: AppColors.bgCard,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: AppColors.border),
      ),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(16),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              // Header: Icon + Device Name + Status Pill
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Container(
                    width: 38,
                    height: 38,
                    decoration: BoxDecoration(
                      color: AppColors.primary.withValues(alpha: 0.12),
                      borderRadius: BorderRadius.circular(10),
                    ),
                    child: const Icon(
                      Icons.developer_board,
                      color: AppColors.primary,
                      size: 20,
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          device.deviceName.isEmpty ? device.deviceId : device.deviceName,
                          style: const TextStyle(
                            color: AppColors.textMain,
                            fontWeight: FontWeight.bold,
                            fontSize: 14,
                          ),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          device.deviceId,
                          style: const TextStyle(
                            fontFamily: 'monospace',
                            color: AppColors.textMuted,
                            fontSize: 11,
                          ),
                        ),
                      ],
                    ),
                  ),
                  Container(
                    padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                    decoration: BoxDecoration(
                      color: (isOnline ? AppColors.statusNormal : AppColors.statusWarning)
                          .withValues(alpha: 0.12),
                      borderRadius: BorderRadius.circular(8),
                      border: Border.all(
                        color: (isOnline ? AppColors.statusNormal : AppColors.statusWarning)
                            .withValues(alpha: 0.4),
                      ),
                    ),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(
                          isOnline ? Icons.wifi : Icons.wifi_off,
                          size: 12,
                          color: isOnline ? AppColors.statusNormal : AppColors.statusWarning,
                        ),
                        const SizedBox(width: 4),
                        Text(
                          isOnline ? 'Trực tuyến' : 'Ngoại tuyến',
                          style: TextStyle(
                            fontSize: 11,
                            fontWeight: FontWeight.w600,
                            color: isOnline ? AppColors.statusNormal : AppColors.statusWarning,
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),

              const SizedBox(height: 12),
              const Divider(color: AppColors.border, height: 1),
              const SizedBox(height: 10),

              // Info grid
              Row(
                children: [
                  Expanded(
                    child: _InfoTile(
                      icon: Icons.terrain_outlined,
                      label: 'Bể nuôi',
                      value: device.terrariumName ?? 'Chưa gán',
                    ),
                  ),
                  Expanded(
                    child: _InfoTile(
                      icon: Icons.memory,
                      label: 'MAC',
                      value: device.macAddress.isNotEmpty ? device.macAddress : '—',
                      monospace: true,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              Row(
                children: [
                  Expanded(
                    child: _InfoTile(
                      icon: Icons.system_update_alt,
                      label: 'Firmware',
                      value: device.firmwareVersion ?? '—',
                    ),
                  ),
                  Expanded(
                    child: _InfoTile(
                      icon: Icons.timer_outlined,
                      label: 'Chu kỳ lấy mẫu',
                      value: '${device.samplingIntervalSec}s',
                    ),
                  ),
                ],
              ),

              if (device.signalStrengthDbm != null || device.batteryPct != null) ...[
                const SizedBox(height: 8),
                Row(
                  children: [
                    if (device.signalStrengthDbm != null)
                      Expanded(
                        child: _InfoTile(
                          icon: Icons.signal_cellular_alt,
                          label: 'Tín hiệu',
                          value: '${device.signalStrengthDbm!.toStringAsFixed(0)} dBm',
                        ),
                      ),
                    if (device.batteryPct != null)
                      Expanded(
                        child: _InfoTile(
                          icon: Icons.battery_charging_full,
                          label: 'Pin',
                          value: '${device.batteryPct!.toStringAsFixed(0)}%',
                        ),
                      ),
                  ],
                ),
              ],

              const SizedBox(height: 12),

              // Action buttons row
              Row(
                mainAxisAlignment: MainAxisAlignment.end,
                children: [
                  if (canWrite)
                    TextButton.icon(
                      style: TextButton.styleFrom(
                        foregroundColor: AppColors.accent,
                        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
                      ),
                      onPressed: onEdit,
                      icon: const Icon(Icons.tune, size: 15),
                      label: const Text('Cấu hình', style: TextStyle(fontSize: 12)),
                    ),
                  if (canProvision)
                    PopupMenuButton<String>(
                      icon: const Icon(Icons.more_vert, size: 18, color: AppColors.textMuted),
                      color: AppColors.bgCard,
                      itemBuilder: (ctx) => [
                        const PopupMenuItem(
                          value: 'rotate',
                          child: Row(
                            children: [
                              Icon(Icons.refresh, size: 16, color: AppColors.accent),
                              SizedBox(width: 8),
                              Text('Đổi khóa bí mật', style: TextStyle(color: AppColors.textMain, fontSize: 13)),
                            ],
                          ),
                        ),
                        const PopupMenuItem(
                          value: 'revoke',
                          child: Row(
                            children: [
                              Icon(Icons.block, size: 16, color: AppColors.statusWarning),
                              SizedBox(width: 8),
                              Text('Thu hồi khóa', style: TextStyle(color: AppColors.statusWarning, fontSize: 13)),
                            ],
                          ),
                        ),
                        const PopupMenuItem(
                          value: 'delete',
                          child: Row(
                            children: [
                              Icon(Icons.delete_outline, size: 16, color: AppColors.statusDanger),
                              SizedBox(width: 8),
                              Text('Gỡ thiết bị', style: TextStyle(color: AppColors.statusDanger, fontSize: 13)),
                            ],
                          ),
                        ),
                      ],
                      onSelected: (action) {
                        if (action == 'rotate') onRotateSecret();
                        if (action == 'revoke') onRevoke();
                        if (action == 'delete') onDelete();
                      },
                    ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _InfoTile extends StatelessWidget {
  const _InfoTile({
    required this.icon,
    required this.label,
    required this.value,
    this.monospace = false,
  });

  final IconData icon;
  final String label;
  final String value;
  final bool monospace;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(icon, size: 14, color: AppColors.textMuted),
        const SizedBox(width: 6),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                label,
                style: const TextStyle(color: AppColors.textMuted, fontSize: 11),
              ),
              const SizedBox(height: 1),
              Text(
                value,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  color: AppColors.textMain,
                  fontSize: 12,
                  fontWeight: FontWeight.w500,
                  fontFamily: monospace ? 'monospace' : null,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

/// Bottom Sheet for Claiming a Device
class _ClaimDeviceSheet extends StatefulWidget {
  const _ClaimDeviceSheet();

  @override
  State<_ClaimDeviceSheet> createState() => _ClaimDeviceSheetState();
}

class _ClaimDeviceSheetState extends State<_ClaimDeviceSheet> {
  final _claimCodeController = TextEditingController();
  final _macController = TextEditingController();
  String? _selectedTerrariumId;
  bool _useMac = false;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _claimCodeController.dispose();
    _macController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final terrariums = context.watch<TerrariumProvider>().terrariums;

    return Padding(
      padding: EdgeInsets.only(
        left: 20,
        right: 20,
        top: 20,
        bottom: MediaQuery.of(context).viewInsets.bottom + 24,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              const Text(
                'Nhận thiết bị IoT',
                style: TextStyle(
                  color: AppColors.textMain,
                  fontWeight: FontWeight.bold,
                  fontSize: 18,
                ),
              ),
              IconButton(
                icon: const Icon(Icons.close, color: AppColors.textMuted),
                onPressed: () => Navigator.pop(context),
              ),
            ],
          ),
          const SizedBox(height: 4),
          const Text(
            'Ghép nối bo mạch cảm biến ESP32 vào một trong các bể nuôi của bạn.',
            style: TextStyle(color: AppColors.textMuted, fontSize: 13),
          ),
          const SizedBox(height: 16),

          // Method toggle
          Row(
            children: [
              ChoiceChip(
                label: const Text('Mã nhận thiết bị'),
                selected: !_useMac,
                selectedColor: AppColors.primary.withValues(alpha: 0.25),
                onSelected: (val) => setState(() => _useMac = !val),
              ),
              const SizedBox(width: 8),
              ChoiceChip(
                label: const Text('Địa chỉ MAC'),
                selected: _useMac,
                selectedColor: AppColors.primary.withValues(alpha: 0.25),
                onSelected: (val) => setState(() => _useMac = val),
              ),
            ],
          ),

          const SizedBox(height: 16),

          if (!_useMac)
            TextField(
              controller: _claimCodeController,
              textCapitalization: TextCapitalization.characters,
              style: const TextStyle(color: AppColors.textMain),
              decoration: InputDecoration(
                labelText: 'Mã nhận thiết bị (8 ký tự)',
                hintText: 'VD: ABCD1234',
                prefixIcon: const Icon(Icons.qr_code, color: AppColors.textMuted),
                filled: true,
                fillColor: AppColors.bgInput,
                border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
              ),
            )
          else
            TextField(
              controller: _macController,
              style: const TextStyle(color: AppColors.textMain, fontFamily: 'monospace'),
              decoration: InputDecoration(
                labelText: 'Địa chỉ MAC bo mạch',
                hintText: 'VD: AA:BB:CC:DD:EE:FF',
                prefixIcon: const Icon(Icons.fingerprint, color: AppColors.textMuted),
                filled: true,
                fillColor: AppColors.bgInput,
                border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
              ),
            ),

          const SizedBox(height: 16),

          // Terrarium dropdown
          DropdownButtonFormField<String>(
            initialValue: _selectedTerrariumId,
            dropdownColor: AppColors.bgCard,
            style: const TextStyle(color: AppColors.textMain),
            decoration: InputDecoration(
              labelText: 'Chọn bể nuôi gắn kết',
              prefixIcon: const Icon(Icons.terrain_outlined, color: AppColors.textMuted),
              filled: true,
              fillColor: AppColors.bgInput,
              border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
            ),
            items: [
              for (final t in terrariums)
                DropdownMenuItem(
                  value: t.id,
                  child: Text(t.name, style: const TextStyle(color: AppColors.textMain)),
                ),
            ],
            onChanged: (val) => setState(() => _selectedTerrariumId = val),
          ),

          if (_error != null) ...[
            const SizedBox(height: 12),
            Text(
              _error!,
              style: const TextStyle(color: AppColors.statusDanger, fontSize: 13),
            ),
          ],

          const SizedBox(height: 20),

          SizedBox(
            width: double.infinity,
            height: 48,
            child: ElevatedButton(
              style: ElevatedButton.styleFrom(
                backgroundColor: AppColors.primary,
                foregroundColor: Colors.white,
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
              ),
              onPressed: _busy ? null : _submitClaim,
              child: _busy
                  ? const SizedBox(
                      width: 20,
                      height: 20,
                      child: CircularProgressIndicator(color: Colors.white, strokeWidth: 2),
                    )
                  : const Text('Nhận thiết bị', style: TextStyle(fontWeight: FontWeight.bold)),
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _submitClaim() async {
    if (_selectedTerrariumId == null) {
      setState(() => _error = 'Vui lòng chọn bể nuôi để gán thiết bị.');
      return;
    }

    final code = _claimCodeController.text.trim();
    final mac = _macController.text.trim();

    if (!_useMac && code.isEmpty) {
      setState(() => _error = 'Vui lòng nhập mã nhận thiết bị.');
      return;
    }
    if (_useMac && mac.isEmpty) {
      setState(() => _error = 'Vui lòng nhập địa chỉ MAC.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      final deviceProv = context.read<DeviceProvider>();
      final ClaimResult result;

      if (_useMac) {
        result = await deviceProv.claimByMac(
          macAddress: mac,
          terrariumId: _selectedTerrariumId!,
        );
      } else {
        result = await deviceProv.claimDevice(
          claimCode: code,
          terrariumId: _selectedTerrariumId!,
        );
      }

      if (mounted) {
        Navigator.pop(context); // close sheet
        _DevicesScreenState._showSecretDialog(
          context,
          result.secret,
          result.deviceId,
        );
      }
    } catch (e) {
      setState(() {
        _busy = false;
        _error = 'Không thể nhận thiết bị: $e';
      });
    }
  }
}

/// Dialog for Editing Device Name & Intervals
class _EditDeviceDialog extends StatefulWidget {
  const _EditDeviceDialog({required this.device});

  final DeviceItem device;

  @override
  State<_EditDeviceDialog> createState() => _EditDeviceDialogState();
}

class _EditDeviceDialogState extends State<_EditDeviceDialog> {
  late final TextEditingController _nameController;
  late final TextEditingController _samplingController;
  late final TextEditingController _publishController;
  String? _terrariumId;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _nameController = TextEditingController(text: widget.device.deviceName);
    _samplingController =
        TextEditingController(text: '${widget.device.samplingIntervalSec}');
    _publishController =
        TextEditingController(text: '${widget.device.publishIntervalSec}');
    _terrariumId = widget.device.terrariumId;
    // Refreshed on open so an enclosure created since the screen loaded is still offered here.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        context.read<TerrariumProvider>().loadTerrariums();
      }
    });
  }

  @override
  void dispose() {
    _nameController.dispose();
    _samplingController.dispose();
    _publishController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final terrariums = context.watch<TerrariumProvider>().terrariums;
    final canBind = context.watch<SessionProvider?>()?.hasPermission(Permissions.deviceProvision) ?? false;

    return AlertDialog(
      backgroundColor: AppColors.bgCard,
      title: const Text('Cấu hình thiết bị', style: TextStyle(color: AppColors.textMain)),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(
              controller: _nameController,
              maxLength: 60,
              style: const TextStyle(color: AppColors.textMain),
              decoration: const InputDecoration(
                labelText: 'Tên thiết bị',
                helperText: 'Tên hiển thị gợi nhớ',
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _samplingController,
              keyboardType: TextInputType.number,
              style: const TextStyle(color: AppColors.textMain),
              decoration: const InputDecoration(
                labelText: 'Chu kỳ lấy mẫu (giây)',
                helperText: 'Từ 10 đến 300 giây',
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _publishController,
              keyboardType: TextInputType.number,
              style: const TextStyle(color: AppColors.textMain),
              decoration: const InputDecoration(
                labelText: 'Chu kỳ gửi dữ liệu (giây)',
                helperText: 'Từ 10 đến 300 giây',
              ),
            ),
            if (canBind) ...[
              const SizedBox(height: 16),
              const Divider(color: AppColors.border),
              const SizedBox(height: 8),
              Row(
                children: [
                  const Icon(Icons.home_work_outlined, size: 16, color: AppColors.textMuted),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      widget.device.terrariumName == null
                          ? 'Thiết bị chưa thuộc bể nuôi nào'
                          : 'Đang ở bể: ${widget.device.terrariumName}',
                      style: const TextStyle(color: AppColors.textMuted, fontSize: 12),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              DropdownButtonFormField<String>(
                initialValue: terrariums.any((item) => item.id == _terrariumId) ? _terrariumId : null,
                dropdownColor: AppColors.bgCard,
                style: const TextStyle(color: AppColors.textMain, fontSize: 14),
                decoration: const InputDecoration(labelText: 'Thêm vào bể nuôi'),
                items: [
                  for (final terrarium in terrariums)
                    DropdownMenuItem(
                      value: terrarium.id,
                      child: Text(terrarium.name),
                    ),
                ],
                onChanged: (value) => setState(() => _terrariumId = value),
              ),
              const SizedBox(height: 8),
              SizedBox(
                width: double.infinity,
                child: OutlinedButton.icon(
                  style: OutlinedButton.styleFrom(
                    foregroundColor: AppColors.accent,
                    side: BorderSide(color: AppColors.accent.withValues(alpha: 0.4)),
                  ),
                  onPressed: _busy ? null : _bind,
                  icon: const Icon(Icons.link, size: 16),
                  label: const Text('Thêm vào bể nuôi'),
                ),
              ),
              const SizedBox(height: 4),
              const Text(
                'Mỗi bể nuôi chỉ chứa một thiết bị: thiết bị đang ở bể đó (nếu có) sẽ được gỡ ra và có thể gắn lại sau.',
                style: TextStyle(color: AppColors.textMuted, fontSize: 11),
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: 10),
              Text(_error!, style: const TextStyle(color: AppColors.statusDanger, fontSize: 12)),
            ],
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(context),
          child: const Text('Huỷ', style: TextStyle(color: AppColors.textMuted)),
        ),
        ElevatedButton(
          style: ElevatedButton.styleFrom(
            backgroundColor: AppColors.primary,
            foregroundColor: Colors.white,
          ),
          onPressed: _busy ? null : _save,
          child: _busy
              ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
              : const Text('Lưu'),
        ),
      ],
    );
  }

  Future<void> _bind() async {
    final terrariumId = _terrariumId;
    if (terrariumId == null || terrariumId.isEmpty) {
      setState(() => _error = 'Chọn bể nuôi trước khi gắn thiết bị.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      await context.read<DeviceProvider>().moveDevice(
            deviceId: widget.device.deviceId,
            terrariumId: terrariumId,
          );
      if (!mounted) return;
      Navigator.pop(context);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Đã gắn thiết bị vào bể nuôi.')),
      );
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _busy = false;
        _error = 'Lỗi gắn thiết bị: $e';
      });
    }
  }

  Future<void> _save() async {
    final name = _nameController.text.trim();
    final sampling = int.tryParse(_samplingController.text.trim()) ?? 60;
    final publish = int.tryParse(_publishController.text.trim()) ?? 60;

    if (sampling < 10 || sampling > 300) {
      setState(() => _error = 'Chu kỳ lấy mẫu phải từ 10 đến 300 giây.');
      return;
    }
    if (publish < 10 || publish > 300) {
      setState(() => _error = 'Chu kỳ gửi dữ liệu phải từ 10 đến 300 giây.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      await context.read<DeviceProvider>().updateDevice(
            widget.device.deviceId,
            deviceName: name,
            samplingIntervalSec: sampling,
            publishIntervalSec: publish,
          );
      if (mounted) {
        Navigator.pop(context);
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Đã cập nhật cấu hình thiết bị.')),
        );
      }
    } catch (e) {
      setState(() {
        _busy = false;
        _error = 'Lỗi lưu cấu hình: $e';
      });
    }
  }
}

/// Dialog displaying full device technical details
class _DeviceDetailDialog extends StatelessWidget {
  const _DeviceDetailDialog({required this.device});

  final DeviceItem device;

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      backgroundColor: AppColors.bgCard,
      title: Row(
        children: [
          const Icon(Icons.developer_board, color: AppColors.primary, size: 22),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              device.deviceName.isEmpty ? device.deviceId : device.deviceName,
              style: const TextStyle(color: AppColors.textMain, fontSize: 16),
            ),
          ),
        ],
      ),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _DetailRow(label: 'Mã thiết bị (ID)', value: device.deviceId, monospace: true),
            _DetailRow(label: 'Địa chỉ MAC', value: device.macAddress, monospace: true),
            _DetailRow(label: 'Chip ID', value: device.chipId, monospace: true),
            _DetailRow(label: 'Giao thức', value: device.protocol),
            _DetailRow(label: 'Trạng thái', value: device.status),
            _DetailRow(label: 'Bể nuôi', value: device.terrariumName ?? 'Chưa gán'),
            _DetailRow(label: 'Firmware', value: device.firmwareVersion ?? '—'),
            _DetailRow(label: 'Chu kỳ lấy mẫu', value: '${device.samplingIntervalSec} giây'),
            _DetailRow(label: 'Chu kỳ gửi', value: '${device.publishIntervalSec} giây'),
            _DetailRow(
              label: 'Tín hiệu Wi-Fi',
              value: device.signalStrengthDbm != null ? '${device.signalStrengthDbm!.toStringAsFixed(0)} dBm' : '—',
            ),
            _DetailRow(
              label: 'Mức pin',
              value: device.batteryPct != null ? '${device.batteryPct!.toStringAsFixed(0)}%' : '—',
            ),
            _DetailRow(label: 'Lần cuối kết nối', value: device.lastSeenAt ?? '—'),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(context),
          child: const Text('Đóng'),
        ),
      ],
    );
  }
}

class _DetailRow extends StatelessWidget {
  const _DetailRow({
    required this.label,
    required this.value,
    this.monospace = false,
  });

  final String label;
  final String value;
  final bool monospace;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 120,
            child: Text(
              label,
              style: const TextStyle(color: AppColors.textMuted, fontSize: 12),
            ),
          ),
          Expanded(
            child: Text(
              value,
              style: TextStyle(
                color: AppColors.textMain,
                fontSize: 12,
                fontWeight: FontWeight.w600,
                fontFamily: monospace ? 'monospace' : null,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _EmptyState extends StatelessWidget {
  const _EmptyState({required this.isSearch, this.onClaimTap});

  final bool isSearch;
  final VoidCallback? onClaimTap;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 48, horizontal: 24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 64,
              height: 64,
              decoration: BoxDecoration(
                color: AppColors.bgCard,
                shape: BoxShape.circle,
                border: Border.all(color: AppColors.border),
              ),
              child: const Icon(
                Icons.memory,
                color: AppColors.textMuted,
                size: 32,
              ),
            ),
            const SizedBox(height: 16),
            Text(
              isSearch ? 'Không tìm thấy thiết bị phù hợp' : 'Chưa có thiết bị nào được kết nối',
              style: const TextStyle(
                color: AppColors.textMain,
                fontSize: 15,
                fontWeight: FontWeight.bold,
              ),
            ),
            const SizedBox(height: 6),
            Text(
              isSearch
                  ? 'Thử thay đổi từ khóa tìm kiếm hoặc bộ lọc trạng thái.'
                  : 'Hãy nhận và ghép nối bo mạch cảm biến ESP32 vào bể nuôi của bạn để bắt đầu giám sát.',
              textAlign: TextAlign.center,
              style: const TextStyle(color: AppColors.textMuted, fontSize: 13),
            ),
            if (!isSearch && onClaimTap != null) ...[
              const SizedBox(height: 18),
              ElevatedButton.icon(
                style: ElevatedButton.styleFrom(
                  backgroundColor: AppColors.primary,
                  foregroundColor: Colors.white,
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                ),
                onPressed: onClaimTap,
                icon: const Icon(Icons.add_link, size: 18),
                label: const Text('Nhận thiết bị ngay'),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _ErrorState extends StatelessWidget {
  const _ErrorState({required this.failure, required this.onRetry});

  final dynamic failure;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.error_outline, size: 40, color: AppColors.statusDanger),
            const SizedBox(height: 12),
            const Text(
              'Không thể tải danh sách thiết bị',
              style: TextStyle(color: AppColors.textMain, fontWeight: FontWeight.bold),
            ),
            const SizedBox(height: 4),
            Text(
              failure.toString(),
              textAlign: TextAlign.center,
              style: const TextStyle(color: AppColors.textMuted, fontSize: 12),
            ),
            const SizedBox(height: 16),
            OutlinedButton.icon(
              onPressed: onRetry,
              icon: const Icon(Icons.refresh, size: 16),
              label: const Text('Thử lại'),
            ),
          ],
        ),
      ),
    );
  }
}

