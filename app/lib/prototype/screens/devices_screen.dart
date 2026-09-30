/// The device fleet (S10), the claim flow (S11) and the diagnostics view (S16).
///
/// They share a file because they share a subject: the honesty of the hardware. The fleet says whether a node is
/// reachable, the claim flow says how a node becomes yours, and diagnostics says how much of the data you expected
/// actually arrived. Splitting them would put three views of the same question in three places.
library;

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../fake_world.dart';
import '../labels.dart';
import '../prototype_state.dart';
import '../widgets/prototype_ui.dart';

/// S10: every node, claimed or not.
class DevicesScreen extends StatelessWidget {
  /// Creates the fleet view.
  const DevicesScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final state = context.watch<PrototypeState>();
    final bound = state.devices.where((device) => device.isClaimed).toList();
    final unbound = state.devices.where((device) => !device.isClaimed).toList();

    return Scaffold(
      appBar: AppBar(
        title: const Text(Labels.devicesTitle),
        actions: [
          IconButton(
            tooltip: Labels.devicesClaim,
            onPressed: () => Navigator.of(context).push(
              MaterialPageRoute<void>(
                builder: (_) => const ClaimDeviceScreen(),
              ),
            ),
            icon: const Icon(Icons.add_link),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 32),
        children: [
          const SectionHeading(Labels.devicesBound),
          for (final device in bound) ...[
            _DeviceCard(device: device),
            const SizedBox(height: 10),
          ],
          if (unbound.isNotEmpty) ...[
            const SizedBox(height: 6),
            const SectionHeading(Labels.devicesUnbound),
            for (final device in unbound) ...[
              _DeviceCard(device: device),
              const SizedBox(height: 10),
            ],
          ],
        ],
      ),
    );
  }
}

class _DeviceCard extends StatelessWidget {
  const _DeviceCard({required this.device});

  final DeviceRecord device;

  @override
  Widget build(BuildContext context) {
    final state = context.watch<PrototypeState>();
    final theme = Theme.of(context);
    final terrarium = device.terrariumId == null
        ? null
        : state.terrariums.firstWhere(
            (candidate) => candidate.id == device.terrariumId,
          );
    final color = switch (device.status) {
      DeviceStatus.online => ProtoColors.inRange,
      DeviceStatus.maintenance => ProtoColors.info,
      DeviceStatus.provisioning => ProtoColors.info,
      DeviceStatus.offline || DeviceStatus.revoked => ProtoColors.unknown,
    };

    return SectionCard(
      title: device.id,
      subtitle: device.serialHint,
      trailing: StatusPill(
        label: device.status.label,
        color: color,
        icon: device.status.isReporting ? Icons.circle : Icons.circle_outlined,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          KeyValueRow(
            label: Labels.devicesBoundTo,
            value: terrarium?.name ?? '—',
            icon: Icons.terrain_outlined,
          ),
          KeyValueRow(
            label: Labels.devicesFirmware,
            value: device.firmwareVersion,
            icon: Icons.memory,
          ),
          KeyValueRow(
            label: Labels.devicesLastSeen,
            value: device.lastSeenAt == null
                ? '—'
                : '${formatInstant(device.lastSeenAt!)} '
                      '(${formatShortDuration(state.demoNow.difference(device.lastSeenAt!))} ago)',
            icon: Icons.access_time,
          ),
          KeyValueRow(
            label: Labels.devicesSignal,
            value: device.rssiDbm == null ? '—' : '${device.rssiDbm} dBm',
            icon: Icons.wifi,
          ),
          KeyValueRow(
            label: Labels.devicesBattery,
            value: device.batteryPct == null
                ? 'mains'
                : '${device.batteryPct} %',
            icon: Icons.battery_5_bar,
          ),
          if (device.uptimeS != null)
            KeyValueRow(
              label: Labels.devicesUptime,
              value: formatShortDuration(Duration(seconds: device.uptimeS!)),
              icon: Icons.timelapse,
            ),
          if (device.heapKb != null)
            KeyValueRow(
              label: Labels.devicesHeap,
              value: '${device.heapKb} kB',
              icon: Icons.sd_storage_outlined,
            ),
          if (device.claimCode != null && !device.isClaimed) ...[
            const SizedBox(height: 8),
            Callout(
              title: 'Pairing code',
              message:
                  '${device.claimCode} · ${Labels.claimCodeExpires(device.claimCodeExpiresAt == null ? '—' : formatShortDuration(device.claimCodeExpiresAt!.difference(state.demoNow)))}',
              color: ProtoColors.info,
              icon: Icons.pin_outlined,
              dense: true,
            ),
          ],
          const SizedBox(height: 10),
          const SectionHeading(Labels.devicesCalibration),
          Text(
            Labels.devicesCalibrationNote,
            style: theme.textTheme.labelSmall,
          ),
          const SizedBox(height: 6),
          _CalibrationRow(device: device),
          const SizedBox(height: 8),
          SwitchListTile.adaptive(
            value: device.status == DeviceStatus.maintenance,
            onChanged: (enabled) => state.setMaintenance(device.id, enabled),
            title: const Text(Labels.devicesMaintenanceSwitch),
            subtitle: const Text(
              Labels.devicesMaintenanceNote,
              style: TextStyle(fontSize: 11),
            ),
            contentPadding: EdgeInsets.zero,
            dense: true,
          ),
          Wrap(
            spacing: 8,
            children: [
              OutlinedButton(
                onPressed: () => state.rebindDevice(
                  device.id,
                  device.terrariumId == null ? state.activeTerrariumId : null,
                ),
                child: Text(
                  device.terrariumId == null
                      ? 'Claim here'
                      : Labels.devicesRebind,
                ),
              ),
              OutlinedButton(
                onPressed: () => _confirmRevoke(context, state, device),
                child: const Text(Labels.devicesRevoke),
              ),
              OutlinedButton(
                onPressed: () {
                  final secret = state.rotateDeviceSecret(device.id);
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(content: Text('New device secret: $secret')),
                  );
                },
                child: const Text(Labels.devicesRotate),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Future<void> _confirmRevoke(
    BuildContext context,
    PrototypeState state,
    DeviceRecord device,
  ) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        content: const Text(Labels.devicesRevokeConfirm),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: const Text(Labels.cancel),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(true),
            child: const Text(Labels.devicesRevoke),
          ),
        ],
      ),
    );
    if (confirmed == true) {
      state.revokeDevice(device.id);
    }
  }
}

class _CalibrationRow extends StatelessWidget {
  const _CalibrationRow({required this.device});

  final DeviceRecord device;

  @override
  Widget build(BuildContext context) {
    final state = context.read<PrototypeState>();
    final theme = Theme.of(context);

    return Wrap(
      spacing: 8,
      runSpacing: 8,
      crossAxisAlignment: WrapCrossAlignment.center,
      children: [
        Text(
          '${Labels.fieldTempOffset}: ${device.tempOffsetC}',
          style: theme.textTheme.labelSmall,
        ),
        Text(
          '${Labels.fieldRhOffset}: ${device.rhOffsetPct}',
          style: theme.textTheme.labelSmall,
        ),
        Text(
          '${Labels.fieldLuxGain}: ${device.luxGain}',
          style: theme.textTheme.labelSmall,
        ),
        OutlinedButton(
          onPressed: () => state.setCalibration(
            device.id,
            tempOffsetC: -0.5,
            rhOffsetPct: 1.5,
            luxGain: 1.02,
          ),
          child: const Text(Labels.save),
        ),
        OutlinedButton(
          onPressed: () => state.setCalibration(
            device.id,
            tempOffsetC: 0,
            rhOffsetPct: 0,
            luxGain: 1,
          ),
          child: const Text('Reset'),
        ),
      ],
    );
  }
}

/// S11: claim a node with its pairing code.
class ClaimDeviceScreen extends StatefulWidget {
  /// Creates the screen.
  const ClaimDeviceScreen({super.key});

  @override
  State<ClaimDeviceScreen> createState() => _ClaimDeviceScreenState();
}

class _ClaimDeviceScreenState extends State<ClaimDeviceScreen> {
  final _code = TextEditingController();
  String _terrariumId = '';
  String _error = '';
  bool _claimed = false;

  @override
  void initState() {
    super.initState();
    final state = context.read<PrototypeState>();
    _terrariumId = state.activeTerrariumId;
    if (state.deviceAwaitingClaim?.claimCode != null) {
      _code.text = state.deviceAwaitingClaim!.claimCode!;
    }
  }

  @override
  void dispose() {
    _code.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final state = context.watch<PrototypeState>();
    final waiting = state.deviceAwaitingClaim;
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(title: const Text(Labels.claimTitle)),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 32),
        children: [
          SectionCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '1. ${Labels.claimStepOne}',
                  style: theme.textTheme.bodySmall,
                ),
                const SizedBox(height: 4),
                Text(
                  '2. ${Labels.claimStepTwo}',
                  style: theme.textTheme.bodySmall,
                ),
                if (waiting != null) ...[
                  const SizedBox(height: 12),
                  Callout(
                    title: 'The node is showing',
                    message:
                        '${waiting.claimCode}   ·   '
                        '${Labels.claimCodeExpires(waiting.claimCodeExpiresAt == null ? '—' : formatShortDuration(waiting.claimCodeExpiresAt!.difference(state.demoNow)))}',
                    color: ProtoColors.info,
                    icon: Icons.pin_outlined,
                  ),
                ] else
                  const Callout(
                    message: 'No node is advertising a code right now.',
                    color: ProtoColors.unknown,
                    icon: Icons.help_outline,
                    dense: true,
                  ),
                const SizedBox(height: 14),
                TextField(
                  controller: _code,
                  textCapitalization: TextCapitalization.characters,
                  decoration: const InputDecoration(
                    labelText: Labels.claimCodeLabel,
                    hintText: Labels.claimCodeHint,
                    border: OutlineInputBorder(),
                  ),
                ),
                const SizedBox(height: 12),
                DropdownButtonFormField<String>(
                  initialValue: _terrariumId,
                  decoration: const InputDecoration(
                    labelText: Labels.claimTerrariumLabel,
                    border: OutlineInputBorder(),
                  ),
                  items: [
                    for (final terrarium in state.terrariums)
                      DropdownMenuItem(
                        value: terrarium.id,
                        child: Text(
                          '${terrarium.name}'
                          '${state.deviceFor(terrarium.id) == null ? '' : ' (has a node)'}',
                        ),
                      ),
                  ],
                  onChanged: (value) =>
                      setState(() => _terrariumId = value ?? _terrariumId),
                ),
                if (_error.isNotEmpty) ...[
                  const SizedBox(height: 10),
                  Callout(
                    message: _error,
                    color: ProtoColors.critical,
                    icon: Icons.error_outline,
                    dense: true,
                  ),
                ],
                if (_claimed) ...[
                  const SizedBox(height: 10),
                  const Callout(
                    message: Labels.claimSuccess,
                    color: ProtoColors.inRange,
                    icon: Icons.check_circle_outline,
                    dense: true,
                  ),
                ],
                const SizedBox(height: 14),
                FilledButton(
                  onPressed: () {
                    final error = state.claimDevice(
                      code: _code.text,
                      terrariumId: _terrariumId,
                    );
                    setState(() {
                      _error = error;
                      _claimed = error.isEmpty;
                    });
                  },
                  child: const Text(Labels.claimAction),
                ),
                const SizedBox(height: 8),
                Text(Labels.claimHelp, style: theme.textTheme.labelSmall),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// S16: the honest, boring numbers.
class DiagnosticsScreen extends StatelessWidget {
  /// Creates the diagnostics view.
  const DiagnosticsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final state = context.watch<PrototypeState>();
    final terrariumId = state.activeTerrariumId;
    final coverage = state.coverage(terrariumId);
    final device = state.deviceFor(terrariumId);
    final result = state.engineResultFor(terrariumId);

    return Scaffold(
      appBar: AppBar(title: const Text(Labels.diagnosticsTitle)),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 32),
        children: [
          SectionCard(
            title: state.activeTerrarium.name,
            child: Column(
              children: [
                KeyValueRow(
                  label: Labels.diagnosticsCoverage,
                  value:
                      '${coverage.coveragePct.toStringAsFixed(1)} %'
                      '${coverage.isBelowTarget ? '  ⚠ below the 98 % target' : ''}',
                  valueColor: coverage.isBelowTarget
                      ? ProtoColors.warning
                      : ProtoColors.inRange,
                ),
                KeyValueRow(
                  label: Labels.diagnosticsLastSample,
                  value: coverage.lastSampleAt == null
                      ? '—'
                      : '${formatInstant(coverage.lastSampleAt!)} '
                            '(${formatShortDuration(state.demoNow.difference(coverage.lastSampleAt!))} ago)',
                ),
                KeyValueRow(
                  label: Labels.diagnosticsInterval,
                  value: '${device?.samplingIntervalSec ?? 60} s',
                ),
                KeyValueRow(
                  label: Labels.diagnosticsReceived,
                  value: '${coverage.samplesReceived}',
                ),
                KeyValueRow(
                  label: Labels.diagnosticsWindow,
                  value: formatShortDuration(
                    Duration(minutes: coverage.windowMinutes),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 12),
          const Callout(
            message: Labels.diagnosticsHonestNote,
            color: ProtoColors.info,
            icon: Icons.info_outline,
            dense: true,
          ),
          if (result != null) ...[
            const SizedBox(height: 14),
            SectionCard(
              title: Labels.labCounters,
              subtitle:
                  'The engine ran over this terrarium\'s whole fake history.',
              child: Column(
                children: [
                  KeyValueRow(
                    label: Labels.labCounterEvaluated,
                    value: '${result.counters.samplesEvaluated}',
                  ),
                  KeyValueRow(
                    label: Labels.labCounterSkipped,
                    value: '${result.counters.samplesSkipped}',
                  ),
                  KeyValueRow(
                    label: Labels.labCounterOpened,
                    value: '${result.counters.warningsOpened}',
                  ),
                  KeyValueRow(
                    label: Labels.labCounterEscalated,
                    value: '${result.counters.escalations}',
                  ),
                  KeyValueRow(
                    label: Labels.labCounterResolved,
                    value: '${result.counters.resolutions}',
                  ),
                  KeyValueRow(
                    label: Labels.labCounterSent,
                    value: '${result.counters.notificationsSent}',
                  ),
                  KeyValueRow(
                    label: Labels.labCounterSuppressed,
                    value: '${result.counters.notificationsSuppressed}',
                  ),
                  KeyValueRow(
                    label: Labels.labCounterBackfilled,
                    value: '${result.counters.backfilledSamples}',
                  ),
                ],
              ),
            ),
          ],
        ],
      ),
    );
  }
}
