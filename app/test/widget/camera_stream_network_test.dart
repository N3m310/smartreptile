import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:smart_reptile/widgets/camera_stream_view.dart';

/// A real 8×8 JPEG, so the bytes crossing the socket are the ones the widget would decode.
const _jpegBase64 =
    '/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAIAAgDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDyqiiivyI/Cj//2Q==';

void main() {
  // Plain `test`, not `testWidgets`: the widget-test binding replaces HttpClient with a stub that answers every
  // request with 400, and the point of this test is the real socket path.
  test('reads JPEG frames from a real multipart MJPEG server', () async {
    final jpeg = base64Decode(_jpegBase64);
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);

    final subscription = server.listen((request) async {
      final response = request.response
        ..statusCode = 200
        ..headers.set(
          HttpHeaders.contentTypeHeader,
          'multipart/x-mixed-replace; boundary=frame',
        )
        ..bufferOutput = false;
      for (var i = 0; i < 3; i++) {
        response.add(
          ascii.encode(
            '--frame\r\nContent-Type: image/jpeg\r\n'
            'Content-Length: ${jpeg.length}\r\n\r\n',
          ),
        );
        response.add(jpeg);
        response.add(ascii.encode('\r\n'));
        await response.flush();
      }
    });

    final client = http.Client();
    try {
      final response = await client.send(
        http.Request(
          'GET',
          Uri.parse('http://127.0.0.1:${server.port}/video'),
        ),
      );

      expect(response.statusCode, 200);
      final boundary = mjpegBoundary(response.headers['content-type']);
      expect(boundary, 'frame');

      final frames = await mjpegFrames(
        response.stream,
        boundary!,
      ).take(3).toList();

      expect(frames, hasLength(3));
      expect(frames.first, jpeg);
      expect(frames.last, jpeg);
    } finally {
      client.close();
      await subscription.cancel();
      await server.close(force: true);
    }
  });
}
