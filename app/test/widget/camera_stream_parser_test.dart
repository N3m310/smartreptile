import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:smart_reptile/widgets/camera_stream_view.dart';

/// Bytes that look like a JPEG to the parser; decoding them is not this test's job.
final _fakeJpeg = <int>[0xFF, 0xD8, 1, 2, 3, 4, 0xFF, 0xD9];

List<int> _part(List<int> jpeg, {bool declareLength = true}) => [
  ...ascii.encode('--frame\r\n'),
  ...ascii.encode('Content-Type: image/jpeg\r\n'),
  if (declareLength) ...ascii.encode('Content-Length: ${jpeg.length}\r\n'),
  ...ascii.encode('\r\n'),
  ...jpeg,
  ...ascii.encode('\r\n'),
];

/// The same part with bare LF framing, which is what ffmpeg's `mpjpeg` muxer emits.
List<int> _lfPart(List<int> jpeg, {bool declareLength = true}) => [
  ...ascii.encode('--frame\n'),
  ...ascii.encode('Content-Type: image/jpeg\n'),
  if (declareLength) ...ascii.encode('Content-Length: ${jpeg.length}\n'),
  ...ascii.encode('\n'),
  ...jpeg,
  ...ascii.encode('\n'),
];

/// Emits [bytes] in fixed-size pieces so a frame can straddle any number of chunks.
Stream<List<int>> _chunked(List<int> bytes, int size) async* {
  for (var start = 0; start < bytes.length; start += size) {
    final end = start + size < bytes.length ? start + size : bytes.length;
    yield bytes.sublist(start, end);
  }
}

void main() {
  group('mjpegBoundary', () {
    test('reads the boundary a stream declares', () {
      expect(
        mjpegBoundary('multipart/x-mixed-replace; boundary=frame'),
        'frame',
      );
      expect(
        mjpegBoundary('multipart/x-mixed-replace;boundary="--myBoundary"'),
        '--myBoundary',
      );
    });

    test('answers null for a plain snapshot response', () {
      expect(mjpegBoundary('image/jpeg'), isNull);
      expect(mjpegBoundary(null), isNull);
    });
  });

  group('mjpegFrames', () {
    test('yields every part whose length the server declared', () async {
      final body = [..._part(_fakeJpeg), ..._part(_fakeJpeg)];

      final frames = await mjpegFrames(
        _chunked(body, body.length),
        'frame',
      ).toList();

      expect(frames, hasLength(2));
      expect(frames.first, _fakeJpeg);
    });

    test('reassembles a frame split across arbitrary chunks', () async {
      final body = [..._part(_fakeJpeg), ..._part(_fakeJpeg)];

      final frames = await mjpegFrames(_chunked(body, 3), 'frame').toList();

      expect(frames, hasLength(2));
      expect(frames.last, _fakeJpeg);
    });

    test('falls back to the next boundary when no length is declared', () async {
      final body = [
        ..._part(_fakeJpeg, declareLength: false),
        ..._part(_fakeJpeg, declareLength: false),
        ..._part(_fakeJpeg, declareLength: false),
      ];

      final frames = await mjpegFrames(
        _chunked(body, body.length),
        'frame',
      ).toList();

      // A part is only complete once the boundary after it has arrived, so the trailing one is still pending.
      expect(frames, hasLength(2));
      expect(frames.first, _fakeJpeg);
    });

    test('emits nothing while only a partial part has arrived', () async {
      final body = _part(_fakeJpeg);
      final truncated = body.sublist(0, body.length - 4);

      final frames = await mjpegFrames(
        _chunked(truncated, truncated.length),
        'frame',
      ).toList();

      expect(frames, isEmpty);
    });

    test('frames a part whose headers end in a bare LF', () async {
      final body = [..._lfPart(_fakeJpeg), ..._lfPart(_fakeJpeg)];

      final frames = await mjpegFrames(
        _chunked(body, body.length),
        'frame',
      ).toList();

      expect(frames, hasLength(2));
      expect(frames.first, _fakeJpeg);
    });

    test('framing without a declared length works with bare LF too', () async {
      final body = [
        ..._lfPart(_fakeJpeg, declareLength: false),
        ..._lfPart(_fakeJpeg, declareLength: false),
      ];

      final frames = await mjpegFrames(
        _chunked(body, body.length),
        'frame',
      ).toList();

      // The image bytes must survive intact: only the framing line ending may be trimmed.
      expect(frames, hasLength(1));
      expect(frames.single, _fakeJpeg);
    });
  });
}
