import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;

/// What the stream is actually doing, so the screen around it can say so instead of pretending.
enum CameraStreamState { connecting, live, offline }

/// Shows a live camera feed from a plain HTTP endpoint.
///
/// Two transports are handled, because the two cameras this project's FR-17 demo can have speak one each:
/// * `multipart/x-mixed-replace` (an ESP32-CAM's `/stream`, IP Webcam's `/video`) — parts are decoded as they arrive
///   and the last complete JPEG is painted.
/// * a plain `image/jpeg` URL (IP Webcam's `/shot.jpg`) — polled, since a snapshot endpoint never pushes.
///
/// Written on `package:http` rather than a platform plugin on purpose: the bytes are already JPEG, so the only work
/// is boundary parsing, and a plugin would add one dependency per platform for the same stream.
class CameraStreamView extends StatefulWidget {
  const CameraStreamView({
    super.key,
    required this.url,
    this.fit = BoxFit.contain,
    this.onStateChanged,
    this.pollInterval = const Duration(milliseconds: 250),
    this.reconnectDelay = const Duration(seconds: 2),
    this.minFrameInterval = const Duration(milliseconds: 80),
    this.firstFrameTimeout = const Duration(seconds: 12),
  });

  /// Endpoint to read. Changing it reconnects without rebuilding the parent.
  final String url;

  final BoxFit fit;

  /// Called on every transition so the parent can render an honest badge.
  final ValueChanged<CameraStreamState>? onStateChanged;

  /// Delay between two requests when [url] answers with a single JPEG instead of a stream.
  final Duration pollInterval;

  /// Delay before reconnecting after the peer closes or the network fails.
  final Duration reconnectDelay;

  /// Frames arriving faster than this are dropped: decoding more than ~12 fps of JPEG costs more than it shows.
  final Duration minFrameInterval;

  /// How long an open response may stay frame-less before the view reports it instead of waiting.
  ///
  /// A server that accepts the request but never sends a complete part is a real failure mode (a wrong content type,
  /// a boundary that does not match, a proxy buffering the stream). Without this, "connecting" was permanent and
  /// indistinguishable from slow.
  final Duration firstFrameTimeout;

  @override
  State<CameraStreamView> createState() => _CameraStreamViewState();
}

class _CameraStreamViewState extends State<CameraStreamView> {
  http.Client? _client;
  Uint8List? _frame;
  CameraStreamState _state = CameraStreamState.connecting;
  String? _error;
  bool _running = false;
  DateTime _lastFrameAt = DateTime.fromMillisecondsSinceEpoch(0);

  @override
  void initState() {
    super.initState();
    _start();
  }

  @override
  void didUpdateWidget(CameraStreamView oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.url != widget.url) {
      _stop();
      _start();
    }
  }

  @override
  void dispose() {
    _stop();
    super.dispose();
  }

  void _start() {
    _running = true;
    unawaited(_run());
  }

  void _stop() {
    _running = false;
    _client?.close();
    _client = null;
  }

  void _transition(CameraStreamState state, {String? error}) {
    if (!mounted) return;
    _error = error;
    if (_state == state) return;
    setState(() => _state = state);
    widget.onStateChanged?.call(state);
  }

  Future<void> _run() async {
    while (_running && mounted) {
      _transition(CameraStreamState.connecting);
      final client = http.Client();
      _client = client;
      final watchdog = Timer(widget.firstFrameTimeout, () {
        if (!_running || !mounted || _state == CameraStreamState.live) return;
        _transition(
          CameraStreamState.offline,
          error: 'Đã kết nối ${widget.url} nhưng không có khung hình nào. '
              'Kiểm tra camera có trả về multipart/x-mixed-replace hoặc image/jpeg hay không.',
        );
      });
      try {
        final response = await _open(client);
        final boundary = mjpegBoundary(response.headers['content-type']);
        if (boundary == null) {
          await _poll(client, response);
        } else {
          await for (final frame in mjpegFrames(response.stream, boundary)) {
            if (!_running || !mounted) return;
            _paint(frame);
          }
        }
      } on Object catch (error) {
        if (_running && mounted) {
          _transition(CameraStreamState.offline, error: '$error');
        }
      } finally {
        watchdog.cancel();
        client.close();
        if (identical(_client, client)) _client = null;
      }
      if (!_running || !mounted) return;
      await Future<void>.delayed(widget.reconnectDelay);
    }
  }

  Future<http.StreamedResponse> _open(http.Client client) async {
    final request = http.Request('GET', Uri.parse(widget.url))
      ..headers['Cache-Control'] = 'no-cache';
    final response = await client.send(request);
    if (response.statusCode != 200) {
      throw http.ClientException(
        'HTTP ${response.statusCode} từ ${widget.url}',
        Uri.parse(widget.url),
      );
    }
    return response;
  }

  /// A snapshot endpoint answers one JPEG per request, so the same client is asked again on [pollInterval].
  Future<void> _poll(http.Client client, http.StreamedResponse first) async {
    var response = first;
    while (_running && mounted) {
      _paint(await _readAll(response));
      await Future<void>.delayed(widget.pollInterval);
      if (!_running || !mounted) return;
      response = await _open(client);
    }
  }

  Future<Uint8List> _readAll(http.StreamedResponse response) async {
    final bytes = BytesBuilder(copy: false);
    await for (final chunk in response.stream) {
      bytes.add(chunk);
    }
    return bytes.takeBytes();
  }

  void _paint(Uint8List frame) {
    if (frame.isEmpty || !mounted) return;
    final now = DateTime.now();
    if (now.difference(_lastFrameAt) < widget.minFrameInterval) return;
    final becameLive = _state != CameraStreamState.live;
    _lastFrameAt = now;
    _error = null;
    _state = CameraStreamState.live;
    setState(() => _frame = frame);
    if (becameLive) widget.onStateChanged?.call(CameraStreamState.live);
  }

  @override
  Widget build(BuildContext context) {
    final frame = _frame;
    if (frame != null) {
      return Image.memory(frame, fit: widget.fit, gaplessPlayback: true);
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
                    const Icon(
                      Icons.videocam_off_outlined,
                      size: 36,
                      color: Colors.white70,
                    ),
                    const SizedBox(height: 10),
                    const Text(
                      'Chưa nhận được hình ảnh',
                      style: TextStyle(color: Colors.white70, fontSize: 13),
                    ),
                    if (_error != null) ...[
                      const SizedBox(height: 6),
                      Text(
                        _error!,
                        textAlign: TextAlign.center,
                        maxLines: 3,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(
                          color: Colors.white38,
                          fontSize: 11,
                        ),
                      ),
                    ],
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
                      'Đang kết nối camera…',
                      style: TextStyle(color: Colors.white70, fontSize: 13),
                    ),
                  ],
                ),
        ),
      ),
    );
  }
}

final RegExp _contentLength = RegExp(
  r'content-length\s*:\s*(\d+)',
  caseSensitive: false,
);

/// Where a part's headers end, and how long the blank line that ends them is.
///
/// `multipart/x-mixed-replace` does not pin the line ending, and servers disagree: IP Webcam and the ESP32-CAM
/// examples send CRLF, while ffmpeg's `mpjpeg` muxer sends a bare LF. A parser that only accepted CRLF framed
/// nothing at all from such a server — silently, because "no complete part yet" and "this stream will never frame"
/// look identical from the inside.
(int, int)? _headerEnd(List<int> bytes, int from) {
  for (var i = from; i < bytes.length - 1; i++) {
    if (bytes[i] == 13 && bytes[i + 1] == 10) {
      if (i + 3 < bytes.length && bytes[i + 2] == 13 && bytes[i + 3] == 10) {
        return (i, 4);
      }
    } else if (bytes[i] == 10 && bytes[i + 1] == 10) {
      return (i, 2);
    }
  }
  return null;
}

/// Length of the line ending that precedes the delimiter at [index].
int _trailingLineEnding(List<int> bytes, int index) {
  if (index >= 2 && bytes[index - 2] == 13 && bytes[index - 1] == 10) return 2;
  if (index >= 1 && bytes[index - 1] == 10) return 1;
  return 0;
}
final RegExp _boundaryParameter = RegExp(
  r'boundary\s*=\s*"?([^";]+)"?',
  caseSensitive: false,
);

/// The `boundary=` parameter of a `multipart/x-mixed-replace` content type, or null when the response is not one.
///
/// Exposed for tests: the parser below only works with the boundary the server declared, and a server that sends no
/// boundary is a snapshot endpoint to be polled instead.
@visibleForTesting
String? mjpegBoundary(String? contentType) {
  if (contentType == null) return null;
  if (!contentType.toLowerCase().contains('multipart')) return null;
  return _boundaryParameter.firstMatch(contentType)?.group(1);
}

/// Splits a `multipart/x-mixed-replace` body into JPEG frames.
///
/// Parts are separated by `--<boundary>`; the length is taken from the part's own `Content-Length` when it has one,
/// which is what both IP Webcam and the ESP32-CAM example send, and from the next boundary otherwise. Chunk
/// boundaries carry no meaning — a frame may arrive in any number of pieces — so the parser buffers until a part is
/// complete and yields only whole JPEGs.
@visibleForTesting
Stream<Uint8List> mjpegFrames(Stream<List<int>> body, String boundary) async* {
  final delimiter = ascii.encode('--$boundary');
  var pending = <int>[];

  await for (final chunk in body) {
    pending.addAll(chunk);
    while (true) {
      final start = _indexOf(pending, delimiter, 0);
      if (start < 0) {
        // Nothing has framed yet: keep the tail rather than growing without bound on a malformed body.
        if (pending.length > 1 << 20) {
          pending = pending.sublist(pending.length - 1024);
        }
        break;
      }

      final headerEnd = _headerEnd(pending, start + delimiter.length);
      if (headerEnd == null) break;

      final headers = ascii.decode(
        pending.sublist(start + delimiter.length, headerEnd.$1),
        allowInvalid: true,
      );
      final dataStart = headerEnd.$1 + headerEnd.$2;
      final declared = _contentLength.firstMatch(headers)?.group(1);

      if (declared != null) {
        final end = dataStart + int.parse(declared);
        if (pending.length < end) break;
        yield Uint8List.fromList(pending.sublist(dataStart, end));
        pending = pending.sublist(end);
      } else {
        final next = _indexOf(pending, delimiter, dataStart);
        if (next < 0) break;
        // The bytes before a delimiter are the framing line ending, not part of the image. Which line ending that is
        // belongs to the same question as the headers above, so it is measured rather than assumed.
        final end = next - _trailingLineEnding(pending, next);
        yield Uint8List.fromList(pending.sublist(dataStart, end));
        pending = pending.sublist(next);
      }
    }
  }
}

int _indexOf(List<int> haystack, List<int> needle, int start) {
  final last = haystack.length - needle.length;
  for (var i = start; i <= last; i++) {
    var matched = true;
    for (var j = 0; j < needle.length; j++) {
      if (haystack[i + j] != needle[j]) {
        matched = false;
        break;
      }
    }
    if (matched) return i;
  }
  return -1;
}
