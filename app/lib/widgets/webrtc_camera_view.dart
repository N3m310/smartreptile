import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_webrtc/flutter_webrtc.dart';

import '../services/camera_signaling.dart';
import '../services/ice_servers.dart';
import 'camera_stream_view.dart';

/// Watches a terrarium's camera through WebRTC.
///
/// The viewer never creates an offer: the camera does, as soon as the server says a viewer arrived. That order
/// matters, because the camera is the only side that knows which tracks exist, and a viewer that offered first would
/// have to guess them.
class WebRtcCameraView extends StatefulWidget {
  const WebRtcCameraView({
    super.key,
    required this.baseUrl,
    required this.accessToken,
    required this.terrariumId,
    this.onStateChanged,
  });

  final String baseUrl;
  final String accessToken;
  final String terrariumId;
  final ValueChanged<CameraStreamState>? onStateChanged;

  @override
  State<WebRtcCameraView> createState() => _WebRtcCameraViewState();
}

class _WebRtcCameraViewState extends State<WebRtcCameraView> {
  /// Deadline for ICE + DTLS to complete.
  static const Duration _iceTimeout = Duration(seconds: 20);

  /// Deadline for the first decoded frame once the peer is connected.
  static const Duration _firstFrameTimeout = Duration(seconds: 8);

  static const Duration _watchTick = Duration(milliseconds: 200);

  final RTCVideoRenderer _renderer = RTCVideoRenderer();
  CameraSignaling? _signaling;
  StreamSubscription<Map<String, dynamic>>? _subscription;
  RTCPeerConnection? _peer;

  CameraStreamState _state = CameraStreamState.connecting;
  String? _error;

  Timer? _watchdog;
  int _watchTicks = 0;
  bool _connected = false;

  /// Whether a decoded frame exists, i.e. whether there is something to look at.
  ///
  /// This — not `onTrack` — is what makes the view live. A peer that negotiated a track but never completed ICE used
  /// to flip the badge green over a black rectangle, which is exactly the state a broken TURN configuration produces.
  bool get _hasFrame =>
      _renderer.videoWidth > 0 && _renderer.videoHeight > 0;

  /// Starts (or restarts) the countdown for the state the view is waiting on, and declares a failure when it expires.
  ///
  /// Waiting is only honest for as long as it is bounded: without a deadline a stream that never arrives leaves the
  /// screen on "connecting" for ever, which reads as a hang rather than as the error it is.
  void _armWatchdog() {
    _cancelWatchdog();
    if (!mounted || _state != CameraStreamState.connecting) return;

    final timeout = _connected ? _firstFrameTimeout : _iceTimeout;
    _watchTicks =
        (timeout.inMilliseconds / _watchTick.inMilliseconds).ceil();
    _watchdog = Timer.periodic(_watchTick, (_) {
      if (!mounted) {
        _cancelWatchdog();
        return;
      }
      if (_hasFrame) {
        _setState(CameraStreamState.live);
        return;
      }
      if (--_watchTicks > 0) return;
      _cancelWatchdog();
      _setState(
        CameraStreamState.offline,
        error: _connected
            ? 'Đã kết nối nhưng camera không gửi được hình ảnh. Kiểm tra máy phát và TURN.'
            : 'Không thiết lập được đường truyền video. Hai máy phải cùng một mạng, hoặc cần TURN server.',
      );
    });
  }

  void _cancelWatchdog() {
    _watchdog?.cancel();
    _watchdog = null;
  }

  @override
  void initState() {
    super.initState();
    unawaited(_start());
  }

  @override
  void didUpdateWidget(WebRtcCameraView oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.terrariumId != widget.terrariumId ||
        oldWidget.accessToken != widget.accessToken) {
      unawaited(_restart());
    }
  }

  @override
  void dispose() {
    _cancelWatchdog();
    _subscription?.cancel();
    unawaited(_signaling?.close());
    unawaited(_peer?.close());
    _renderer.dispose();
    super.dispose();
  }

  Future<void> _restart() async {
    await _teardownPeer();
    await _subscription?.cancel();
    await _signaling?.close();
    _signaling = null;
    await _start();
  }

  Future<void> _start() async {
    if (widget.accessToken.isEmpty) {
      _setState(CameraStreamState.offline, error: 'Chưa đăng nhập.');
      return;
    }

    await _renderer.initialize();

    final signaling = CameraSignaling(
      baseUrl: widget.baseUrl,
      accessToken: widget.accessToken,
      terrariumId: widget.terrariumId,
      role: CameraSignalRole.viewer,
    );
    _signaling = signaling;
    _subscription = signaling.events.listen(_onMessage);

    _setState(CameraStreamState.connecting);

    try {
      await signaling.connect();
    } on Object catch (error) {
      _setState(CameraStreamState.offline, error: 'Không mở được kênh tín hiệu: $error');
    }
  }

  Future<void> _onMessage(Map<String, dynamic> message) async {
    switch (message['type']) {
      case 'camera-present':
      case 'camera-joined':
        // The offer follows; nothing to do but say the camera exists.
        _setState(CameraStreamState.connecting);
      case 'camera-left':
        await _teardownPeer();
        _setState(CameraStreamState.offline, error: 'Camera đã ngắt kết nối.');
      case 'offer':
        await _answer(message);
      case 'ice':
        await _addCandidate(message['candidate']);
      case 'error':
        _setState(CameraStreamState.offline, error: '${message['code']}');
      case 'socket-error':
        _setState(CameraStreamState.offline, error: '${message['error']}');
      case 'socket-closed':
        _setState(CameraStreamState.offline, error: 'Mất kết nối tới máy chủ.');
    }
  }

  Future<RTCPeerConnection> _ensurePeer() async {
    final existing = _peer;
    if (existing != null) return existing;

    final peer = await createPeerConnection(iceConfiguration());
    _peer = peer;

    peer.onTrack = (event) {
      final stream = event.streams.isNotEmpty ? event.streams.first : null;
      if (stream == null) return;
      // A negotiated track is not a picture. ICE and DTLS still have to complete, and the decoder still has to
      // produce a frame; until then the view is a black rectangle. The watchdog below is what declares it live.
      _renderer.srcObject = stream;
    };

    peer.onIceCandidate = (candidate) {
      _signaling?.send({
        'type': 'ice',
        'candidate': candidate.toMap(),
      });
    };

    peer.onConnectionState = (connectionState) {
      if (connectionState == RTCPeerConnectionState.RTCPeerConnectionStateConnected) {
        _connected = true;
        _armWatchdog();
        return;
      }
      if (connectionState == RTCPeerConnectionState.RTCPeerConnectionStateFailed ||
          connectionState == RTCPeerConnectionState.RTCPeerConnectionStateDisconnected ||
          connectionState == RTCPeerConnectionState.RTCPeerConnectionStateClosed) {
        _connected = false;
        _cancelWatchdog();
        _setState(CameraStreamState.offline, error: 'Kết nối video bị ngắt.');
      }
    };

    return peer;
  }

  Future<void> _answer(Map<String, dynamic> message) async {
    final sdp = message['sdp'] as String?;
    if (sdp == null) return;

    try {
      final peer = await _ensurePeer();
      await peer.setRemoteDescription(RTCSessionDescription(sdp, 'offer'));
      final answer = await peer.createAnswer();
      await peer.setLocalDescription(answer);
      _signaling?.send({'type': 'answer', 'sdp': answer.sdp});
    } on Object catch (error) {
      _setState(CameraStreamState.offline, error: 'Không tạo được câu trả lời: $error');
    }
  }

  Future<void> _addCandidate(dynamic candidate) async {
    if (candidate is! Map) return;
    final peer = _peer;
    if (peer == null) return;

    try {
      await peer.addCandidate(
        RTCIceCandidate(
          candidate['candidate'] as String?,
          candidate['sdpMid'] as String?,
          candidate['sdpMLineIndex'] as int?,
        ),
      );
    } on Object {
      // A candidate that arrives before the description it belongs to is expected; the connection still forms.
    }
  }

  Future<void> _teardownPeer() async {
    _cancelWatchdog();
    _connected = false;
    final peer = _peer;
    _peer = null;
    if (peer != null) {
      await peer.close();
    }
    _renderer.srcObject = null;
  }

  void _setState(CameraStreamState state, {String? error}) {
    if (!mounted) return;
    setState(() {
      _state = state;
      _error = error;
    });
    _armWatchdog();
    widget.onStateChanged?.call(state);
  }

  @override
  Widget build(BuildContext context) {
    final stream = _renderer.srcObject;
    if (stream != null) {
      return RTCVideoView(_renderer, objectFit: RTCVideoViewObjectFit.RTCVideoViewObjectFitContain);
    }

    return ColoredBox(
      color: Colors.black,
      child: Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: _state == CameraStreamState.offline
              ? Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const Icon(Icons.videocam_off_outlined, size: 36, color: Colors.white70),
                    const SizedBox(height: 10),
                    const Text(
                      'Chưa có hình ảnh',
                      style: TextStyle(color: Colors.white70, fontSize: 13),
                    ),
                    if (_error != null) ...[
                      const SizedBox(height: 6),
                      Text(
                        _error!,
                        textAlign: TextAlign.center,
                        maxLines: 3,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(color: Colors.white38, fontSize: 11),
                      ),
                    ],
                    const SizedBox(height: 10),
                    const Text(
                      'Hãy mở ứng dụng trên điện thoại và bật chế độ phát camera cho bể này.',
                      textAlign: TextAlign.center,
                      style: TextStyle(color: Colors.white24, fontSize: 11),
                    ),
                  ],
                )
              : const Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    SizedBox(
                      width: 26,
                      height: 26,
                      child: CircularProgressIndicator(strokeWidth: 2.4),
                    ),
                    SizedBox(height: 10),
                    Text(
                      'Đang chờ camera…',
                      style: TextStyle(color: Colors.white70, fontSize: 13),
                    ),
                  ],
                ),
        ),
      ),
    );
  }
}
