// Flutter web bootstrap for this machine.
//
// The engine fetches CanvasKit from `gstatic.com` by default, and that host is effectively unreachable here
// (~43 KB/s, then a timeout). The symptom is brutally unhelpful: the page stays **blank white** with no red error
// screen, because the failure happens before Flutter can draw anything.
//
// Pointing `canvasKitBaseUrl` at the local copy fixes it. Both the dev server and `flutter build web` serve
// `/canvaskit/`, so the same file works in debug and release. Verify with:
//   curl -s http://localhost:<port>/flutter_bootstrap.js | grep canvasKitBaseUrl
// A build that still points at gstatic has lost this file.
{{flutter_js}}
{{flutter_build_config}}

_flutter.loader.load({
  config: {
    canvasKitBaseUrl: "/canvaskit/",
  },
});
