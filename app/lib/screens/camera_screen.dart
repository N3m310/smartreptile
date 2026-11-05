import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../core/env.dart';
import '../data/api_client.dart';
import '../state/app_data_provider.dart';
import '../theme/app_theme.dart';
import '../widgets/camera_stream_view.dart';
import '../widgets/webrtc_camera_view.dart';
import 'camera_publisher_screen.dart';

/// S15 — live camera for one enclosure.
///
/// Two transports, because the demo has two ways to point a lens at a terrarium:
/// * **WebRTC** — a second phone (or the emulator) publishes through the API's signalling socket; the video is
///   peer-to-peer, so it works in a release build too. This is the default.
/// * **URL/MJPEG** — any plain HTTP camera endpoint (IP Webcam's `/video`, an ESP32-CAM's `/stream`). No signalling,
///   no peers: the fallback when only a stream URL exists.
///
/// FR-17 itself asks for a manual snapshot; this screen is the live view the demo needs, and which mode is showing is
/// stated rather than implied.
class CameraScreen extends StatefulWidget {
  const CameraScreen({super.key, this.terrariumId, this.initialUrl});

  /// Terrarium whose camera is watched. When absent the first loaded terrarium is used.
  final String? terrariumId;

  /// Pre-filled MJPEG URL. When absent the screen uses the last URL it saved, then [Env.cameraStreamUrl].
  final String? initialUrl;

  @override
  State<CameraScreen> createState() => _CameraScreenState();
}

enum _CameraMode { webrtc, url }

class _CameraScreenState extends State<CameraScreen> {
  static const _urlKey = 'camera.streamUrl';

  final TextEditingController _urlController = TextEditingController();

  _CameraMode _mode = _CameraMode.webrtc;
  CameraStreamState _state = CameraStreamState.connecting;
  String? _terrariumId;
  String? _error;
  bool _urlActive = false;

  @override
  void initState() {
    super.initState();
    _terrariumId = widget.terrariumId;
    _restoreUrl();
  }

  @override
  void dispose() {
    _urlController.dispose();
    super.dispose();
  }

  Future<void> _restoreUrl() async {
    var url = widget.initialUrl ?? '';
    if (url.isEmpty) {
      try {
        final preferences = await SharedPreferences.getInstance();
        url = preferences.getString(_urlKey) ?? '';
      } on Exception {
        // No storage in this environment (a widget test): the build-time default is still a valid answer.
      }
    }
    if (url.isEmpty) url = Env.cameraStreamUrl;
    if (!mounted) return;
    setState(() {
      _urlController.text = url;
      _urlActive = url.isNotEmpty;
    });
  }

  Future<void> _connectUrl() async {
    final url = _urlController.text.trim();
    final uri = Uri.tryParse(url);
    if (uri == null ||
        !uri.hasAuthority ||
        (uri.scheme != 'http' && uri.scheme != 'https')) {
      setState(() {
        _urlActive = false;
        _error = 'Nhập URL http(s) của camera, ví dụ http://192.168.1.20:8080/video';
      });
      return;
    }

    try {
      final preferences = await SharedPreferences.getInstance();
      await preferences.setString(_urlKey, url);
    } on Exception {
      // Persisting is a convenience; a session that cannot store the URL still streams.
    }
    if (!mounted) return;
    setState(() {
      _urlController.text = url;
      _error = null;
      _state = CameraStreamState.connecting;
      _urlActive = true;
    });
  }

  void _onState(CameraStreamState state) {
    if (!mounted || _state == state) return;
    setState(() => _state = state);
  }

  @override
  Widget build(BuildContext context) {
    final terrariums = context.watch<AppDataProvider>().terrariums;
    final selected = _terrariumId ?? (terrariums.isEmpty ? null : terrariums.first.id);

    return Scaffold(
      backgroundColor: AppColors.bgMain,
      appBar: AppBar(
        title: const Text('Camera trực tiếp'),
        actions: [
          IconButton(
            tooltip: 'Phát camera từ điện thoại này',
            icon: const Icon(Icons.videocam_outlined),
            onPressed: () => _openPublisher(selected),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          SegmentedButton<_CameraMode>(
            segments: const [
              ButtonSegment(
                value: _CameraMode.webrtc,
                icon: Icon(Icons.cast, size: 18),
                label: Text('WebRTC'),
              ),
              ButtonSegment(
                value: _CameraMode.url,
                icon: Icon(Icons.link, size: 18),
                label: Text('URL (MJPEG)'),
              ),
            ],
            selected: {_mode},
            onSelectionChanged: (selection) => setState(() {
              _mode = selection.first;
              _error = null;
            }),
          ),
          const SizedBox(height: 12),
          if (_mode == _CameraMode.webrtc)
            _webrtcBody(terrariums, selected)
          else
            _urlBody(),
          const SizedBox(height: 16),
          _helpCard(),
        ],
      ),
    );
  }

  void _openPublisher(String? terrariumId) {
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => CameraPublisherScreen(initialTerrariumId: terrariumId),
      ),
    );
  }

  Widget _webrtcBody(List<dynamic> terrariums, String? selected) {
    final api = context.read<ApiClient>();
    final token = api.accessToken;
    final canWatch = selected != null && token != null && token.isNotEmpty;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (terrariums.length > 1)
          DropdownButtonFormField<String>(
            initialValue: selected,
            decoration: const InputDecoration(
              labelText: 'Bể nuôi',
              prefixIcon: Icon(Icons.pets, size: 18),
            ),
            items: [
              for (final terrarium in terrariums)
                DropdownMenuItem(
                  value: terrarium.id as String,
                  child: Text(
                    terrarium.name as String,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
            ],
            onChanged: (value) => setState(() {
              _terrariumId = value;
              _state = CameraStreamState.connecting;
            }),
          ),
        const SizedBox(height: 10),
        AspectRatio(
          aspectRatio: 16 / 9,
          child: Container(
            decoration: BoxDecoration(
              color: Colors.black,
              borderRadius: BorderRadius.circular(20),
              border: Border.all(color: AppColors.border),
            ),
            clipBehavior: Clip.antiAlias,
            child: Stack(
              fit: StackFit.expand,
              children: [
                if (canWatch)
                  WebRtcCameraView(
                    // Rebuilt when the enclosure or the session changes: that is exactly when an old peer
                    // connection would be showing a different terrarium.
                    key: ValueKey('$selected/$token'),
                    baseUrl: api.baseUrl,
                    accessToken: token,
                    terrariumId: selected,
                    onStateChanged: _onState,
                  )
                else
                  _missingSession(),
                Positioned(top: 10, left: 10, child: _statusChip()),
              ],
            ),
          ),
        ),
        const SizedBox(height: 10),
        const Text(
          'Camera là một điện thoại thứ hai: trên máy đó mở màn hình này, chọn cùng bể nuôi và bật camera. '
          'Hình ảnh đi trực tiếp giữa hai máy; máy chủ chỉ trao đổi thông tin kết nối.',
          style: TextStyle(color: AppColors.textMuted, fontSize: 11, height: 1.4),
        ),
        const SizedBox(height: 4),
        Align(
          alignment: Alignment.centerLeft,
          child: TextButton.icon(
            onPressed: () => _openPublisher(selected),
            icon: const Icon(Icons.videocam_outlined, size: 18),
            label: const Text('Dùng máy này làm camera'),
          ),
        ),
      ],
    );
  }

  Widget _missingSession() => const ColoredBox(
        color: Colors.black,
        child: Center(
          child: Padding(
            padding: EdgeInsets.all(24),
            child: Text(
              'Cần đăng nhập và có ít nhất một bể nuôi để xem camera.',
              textAlign: TextAlign.center,
              style: TextStyle(color: Colors.white54, fontSize: 12),
            ),
          ),
        ),
      );

  Widget _urlBody() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        AspectRatio(
          aspectRatio: 16 / 9,
          child: Container(
            decoration: BoxDecoration(
              color: Colors.black,
              borderRadius: BorderRadius.circular(20),
              border: Border.all(color: AppColors.border),
            ),
            clipBehavior: Clip.antiAlias,
            child: Stack(
              fit: StackFit.expand,
              children: [
                if (_urlActive)
                  CameraStreamView(url: _urlController.text, onStateChanged: _onState)
                else
                  const ColoredBox(color: Colors.black),
                Positioned(top: 10, left: 10, child: _statusChip()),
              ],
            ),
          ),
        ),
        const SizedBox(height: 12),
        TextField(
          controller: _urlController,
          keyboardType: TextInputType.url,
          autocorrect: false,
          style: const TextStyle(fontSize: 13),
          decoration: const InputDecoration(
            labelText: 'Địa chỉ luồng MJPEG',
            hintText: 'http://192.168.1.20:8080/video',
            prefixIcon: Icon(Icons.link, size: 18),
          ),
          onSubmitted: (_) => _connectUrl(),
        ),
        const SizedBox(height: 10),
        FilledButton.icon(
          onPressed: _connectUrl,
          icon: const Icon(Icons.play_arrow),
          label: const Text('Bắt đầu xem'),
          style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(44)),
        ),
        if (_error != null) ...[
          const SizedBox(height: 10),
          Text(
            _error!,
            style: const TextStyle(color: AppColors.statusDanger, fontSize: 12),
          ),
        ],
      ],
    );
  }

  Widget _statusChip() {
    final (label, color) = switch (_state) {
      CameraStreamState.live => ('TRỰC TIẾP', AppColors.statusNormal),
      CameraStreamState.connecting => ('ĐANG KẾT NỐI', AppColors.statusWarning),
      CameraStreamState.offline => ('MẤT KẾT NỐI', AppColors.statusDanger),
    };

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: Colors.black.withValues(alpha: 0.6),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: color, width: 1),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 7,
            height: 7,
            decoration: BoxDecoration(color: color, shape: BoxShape.circle),
          ),
          const SizedBox(width: 6),
          Text(
            label,
            style: TextStyle(
              color: color,
              fontSize: 10,
              fontWeight: FontWeight.bold,
              letterSpacing: 0.6,
            ),
          ),
        ],
      ),
    );
  }

  Widget _helpCard() {
    return Container(
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
            'Hai cách đưa camera vào bể',
            style: TextStyle(
              fontSize: 14,
              fontWeight: FontWeight.bold,
              color: AppColors.textMain,
            ),
          ),
          const SizedBox(height: 10),
          _step('1', 'WebRTC: cài app trên điện thoại thứ hai, đăng nhập cùng tài khoản, mở màn hình này và bật camera.'),
          _step('2', 'Máy xem chỉ cần chọn đúng bể nuôi; cả hai máy phải cùng một mạng Wi-Fi.'),
          _step('3', 'Hoặc URL/MJPEG: cài "IP Webcam" trên điện thoại, bấm Start server rồi nhập http://<ip>:8080/video.'),
          const SizedBox(height: 10),
          const Text(
            'Bản debug cho phép http:// tới mọi địa chỉ trong LAN. Bản release chỉ cho phép http:// tới 10.0.2.2, '
            'localhost và 127.0.0.1, nên camera thật dùng https hoặc WebRTC.',
            style: TextStyle(fontSize: 11, height: 1.4, color: AppColors.textMuted),
          ),
        ],
      ),
    );
  }

  Widget _step(String number, String text) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 6),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 18,
            height: 18,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: AppColors.primary.withValues(alpha: 0.2),
              borderRadius: BorderRadius.circular(6),
            ),
            child: Text(
              number,
              style: const TextStyle(
                fontSize: 10,
                fontWeight: FontWeight.bold,
                color: AppColors.primary,
              ),
            ),
          ),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              text,
              style: const TextStyle(
                fontSize: 12,
                height: 1.35,
                color: AppColors.textMuted,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
