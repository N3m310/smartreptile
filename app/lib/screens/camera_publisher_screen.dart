import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_webrtc/flutter_webrtc.dart';
import 'package:provider/provider.dart';

import '../data/api_client.dart';
import '../services/camera_signaling.dart';
import '../services/ice_servers.dart';
import '../state/app_data_provider.dart';
import '../theme/app_theme.dart';

/// Turns this phone into the camera for one terrarium.
///
/// This is the side that holds the lens, so it creates the offer: the server tells it when a viewer arrives, and it
/// builds one peer connection per viewer. Running it on a second device is the whole point — the demo's camera is a
/// phone standing next to the enclosure, not the machine showing the dashboard.
class CameraPublisherScreen extends StatefulWidget {
  const CameraPublisherScreen({super.key, this.initialTerrariumId});

  final String? initialTerrariumId;

  @override
  State<CameraPublisherScreen> createState() => _CameraPublisherScreenState();
}

class _CameraPublisherScreenState extends State<CameraPublisherScreen> {
  final RTCVideoRenderer _localRenderer = RTCVideoRenderer();

  CameraSignaling? _signaling;
  StreamSubscription<Map<String, dynamic>>? _subscription;
  MediaStream? _localStream;

  /// One peer connection per viewer, keyed by the viewer id the server assigned.
  final Map<String, RTCPeerConnection> _peers = {};

  String? _terrariumId;
  String? _status;
  bool _busy = false;
  bool _publishing = false;

  @override
  void initState() {
    super.initState();
    _terrariumId = widget.initialTerrariumId;
  }

  @override
  void dispose() {
    _subscription?.cancel();
    unawaited(_stop());
    _localRenderer.dispose();
    super.dispose();
  }

  Future<void> _start() async {
    final terrariumId = _terrariumId;
    if (terrariumId == null || terrariumId.isEmpty) {
      setState(() => _status = 'Chọn một bể nuôi trước.');
      return;
    }

    final api = context.read<ApiClient>();
    final token = api.accessToken;
    if (token == null || token.isEmpty) {
      setState(() => _status = 'Chưa đăng nhập.');
      return;
    }

    setState(() {
      _busy = true;
      _status = null;
    });

    try {
      await _localRenderer.initialize();

      final stream = await navigator.mediaDevices.getUserMedia({
        'video': {
          'facingMode': 'environment',
          'width': {'ideal': 1280},
          'height': {'ideal': 720},
        },
        'audio': false,
      });
      _localStream = stream;
      _localRenderer.srcObject = stream;

      final signaling = CameraSignaling(
        baseUrl: api.baseUrl,
        accessToken: token,
        terrariumId: terrariumId,
        role: CameraSignalRole.camera,
      );
      _signaling = signaling;
      _subscription = signaling.events.listen(_onMessage);
      await signaling.connect();

      if (!mounted) return;
      setState(() {
        _publishing = true;
        _status = 'Đang phát. Mở ứng dụng trên máy khác để xem.';
      });
    } on Object catch (error) {
      if (!mounted) return;
      setState(() => _status = 'Không bật được camera: $error');
      await _stop();
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _stop() async {
    await _subscription?.cancel();
    _subscription = null;
    await _signaling?.close();
    _signaling = null;

    for (final peer in _peers.values) {
      await peer.close();
    }
    _peers.clear();

    final stream = _localStream;
    _localStream = null;
    _localRenderer.srcObject = null;
    if (stream != null) {
      for (final track in stream.getTracks()) {
        await track.stop();
      }
      await stream.dispose();
    }

    if (mounted) {
      setState(() {
        _publishing = false;
        _status = null;
      });
    }
  }

  Future<void> _onMessage(Map<String, dynamic> message) async {
    switch (message['type']) {
      case 'viewer-joined':
        final viewerId = message['viewerId'] as String?;
        if (viewerId != null) await _offerTo(viewerId);
      case 'viewer-left':
        final viewerId = message['viewerId'] as String?;
        final peer = _peers.remove(viewerId);
        await peer?.close();
      case 'answer':
        final viewerId = message['viewerId'] as String?;
        final sdp = message['sdp'] as String?;
        final peer = viewerId == null ? null : _peers[viewerId];
        if (peer != null && sdp != null) {
          await peer.setRemoteDescription(RTCSessionDescription(sdp, 'answer'));
        }
      case 'ice':
        final viewerId = message['viewerId'] as String?;
        final candidate = message['candidate'];
        final peer = viewerId == null ? null : _peers[viewerId];
        if (peer != null && candidate is Map) {
          try {
            await peer.addCandidate(
              RTCIceCandidate(
                candidate['candidate'] as String?,
                candidate['sdpMid'] as String?,
                candidate['sdpMLineIndex'] as int?,
              ),
            );
          } on Object {
            // Same tolerance as the viewer: an early candidate is not an error.
          }
        }
      case 'error':
        if (!mounted) return;
        setState(() => _status = 'Máy chủ từ chối: ${message['code']}');
        await _stop();
      case 'socket-error':
        if (!mounted) return;
        setState(() => _status = 'Lỗi kênh tín hiệu: ${message['error']}');
    }
  }

  Future<void> _offerTo(String viewerId) async {
    final stream = _localStream;
    if (stream == null) return;

    final existing = _peers.remove(viewerId);
    await existing?.close();

    final peer = await createPeerConnection(iceConfiguration());
    _peers[viewerId] = peer;

    peer.onIceCandidate = (candidate) {
      _signaling?.send({
        'type': 'ice',
        'candidate': candidate.toMap(),
        'target': viewerId,
      });
    };

    peer.onConnectionState = (connectionState) {
      if (connectionState == RTCPeerConnectionState.RTCPeerConnectionStateConnected) {
        if (mounted) setState(() => _status = 'Đang phát tới $viewerId.');
      }
    };

    for (final track in stream.getTracks()) {
      await peer.addTrack(track, stream);
    }

    final offer = await peer.createOffer();
    await peer.setLocalDescription(offer);
    _signaling?.send({
      'type': 'offer',
      'sdp': offer.sdp,
      'target': viewerId,
    });
  }

  @override
  Widget build(BuildContext context) {
    final terrariums = context.watch<AppDataProvider>().terrariums;

    return Scaffold(
      backgroundColor: AppColors.bgMain,
      appBar: AppBar(title: const Text('Phát camera từ điện thoại này')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Container(
            decoration: BoxDecoration(
              color: Colors.black,
              borderRadius: BorderRadius.circular(20),
              border: Border.all(color: AppColors.border),
            ),
            clipBehavior: Clip.antiAlias,
            child: AspectRatio(
              aspectRatio: 16 / 9,
              child: _localRenderer.srcObject == null
                  ? const Center(
                      child: Text(
                        'Camera đang tắt',
                        style: TextStyle(color: Colors.white54, fontSize: 13),
                      ),
                    )
                  : RTCVideoView(
                      _localRenderer,
                      mirror: false,
                      objectFit: RTCVideoViewObjectFit.RTCVideoViewObjectFitCover,
                    ),
            ),
          ),
          const SizedBox(height: 14),
          if (!_publishing) ...[
            DropdownButtonFormField<String>(
              initialValue: _terrariumId,
              decoration: const InputDecoration(
                labelText: 'Bể nuôi',
                prefixIcon: Icon(Icons.pets, size: 18),
              ),
              items: [
                for (final terrarium in terrariums)
                  DropdownMenuItem(
                    value: terrarium.id,
                    child: Text(terrarium.name, overflow: TextOverflow.ellipsis),
                  ),
              ],
              onChanged: _busy ? null : (value) => setState(() => _terrariumId = value),
            ),
            const SizedBox(height: 12),
            FilledButton.icon(
              onPressed: _busy ? null : _start,
              icon: const Icon(Icons.videocam),
              label: Text(_busy ? 'Đang bật camera…' : 'Bật camera'),
              style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(44)),
            ),
          ] else
            OutlinedButton.icon(
              onPressed: _stop,
              icon: const Icon(Icons.stop_circle_outlined),
              label: const Text('Tắt camera'),
              style: OutlinedButton.styleFrom(minimumSize: const Size.fromHeight(44)),
            ),
          if (_status != null) ...[
            const SizedBox(height: 12),
            Text(
              _status!,
              style: const TextStyle(color: AppColors.textMuted, fontSize: 12, height: 1.4),
            ),
          ],
          const SizedBox(height: 16),
          const Text(
            'Điện thoại này giữ hình ảnh và phát trực tiếp cho người xem. Máy chủ chỉ trao đổi thông tin kết nối, '
            'không nhận hay lưu video.',
            style: TextStyle(color: AppColors.textMuted, fontSize: 11, height: 1.4),
          ),
        ],
      ),
    );
  }
}
