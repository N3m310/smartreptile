import 'dart:convert';
import 'dart:io';

/// Turns a `flutter test --file-reporter json:<file>` run into the Markdown report the web suite writes.
///
/// `flutter test` has no `show-report`: the JSON reporter is the only machine-readable output, so the report is
/// produced here, from that file, in the same shape as `web/e2e/TEST-RESULTS.md` — one row per scenario with its
/// status and duration. Run it after the suite; `tool/run_e2e.ps1` does both in one command.
///
/// ```powershell
/// flutter test integration_test/app_e2e_test.dart -d emulator-5554 --file-reporter json:build/e2e-report.json
/// dart run tool/e2e_report.dart build/e2e-report.json --out integration_test/TEST-RESULTS.md
/// ```
Future<void> main(List<String> args) async {
  final options = _parseArgs(args);
  final jsonPath = options.jsonPath;
  final outPath = options.outPath;

  final file = File(jsonPath);
  if (!file.existsSync()) {
    stderr.writeln('No test report at $jsonPath — run the suite with '
        '`--file-reporter json:$jsonPath` first.');
    exitCode = 2;
    return;
  }

  final tests = <int, _TestRun>{};
  final order = <int>[];
  var totalMs = 0;
  var success = true;

  for (final line in file.readAsLinesSync()) {
    if (line.trim().isEmpty) continue;
    final event = jsonDecode(line) as Map<String, dynamic>;
    switch (event['type']) {
      case 'testStart':
        final test = event['test'] as Map<String, dynamic>;
        final id = test['id'] as int;
        tests[id] = _TestRun(
          name: test['name'] as String? ?? '',
          startedAtMs: (event['time'] as num).toInt(),
        );
        order.add(id);
      case 'testDone':
        final test = tests[event['testID'] as int];
        if (test == null) break;
        // `hidden` marks the pseudo-tests the runner invents for loading and for setUpAll/tearDownAll, which
        // would otherwise show up as scenarios nobody wrote.
        test.hidden = event['hidden'] == true;
        test.skipped = event['skipped'] == true;
        test.result = event['result'] as String? ?? 'error';
        test.durationMs = (event['time'] as num).toInt() - test.startedAtMs;
      case 'error':
        final test = tests[event['testID'] as int];
        if (test != null) {
          test.error ??= (event['error'] as String? ?? '').trim();
        }
      case 'done':
        success = event['success'] == true;
        totalMs = (event['time'] as num).toInt();
    }
  }

  final scenarios = order
      .map((id) => tests[id]!)
      .where((test) => !test.hidden)
      .toList();
  final passed = scenarios.where((test) => _status(test) == 'passed').length;
  final failed = scenarios.where((test) => _status(test) == 'failed').length;
  final skipped = scenarios.where((test) => _status(test) == 'skipped').length;

  final runStartUtc =
      DateTime.now().toUtc().subtract(Duration(milliseconds: totalMs));

  final buffer = StringBuffer()
    ..writeln('# Flutter mobile E2E test results')
    ..writeln()
    ..writeln('- Run at (UTC): ${runStartUtc.toIso8601String()}')
    ..writeln('- Overall status: **${success ? 'passed' : 'failed'}**')
    ..writeln('- Runtime: Dart ${_dartVersion()} (${Platform.operatingSystem})')
    ..writeln('- Tests: ${scenarios.length} total; $passed passed; '
        '$failed failed; $skipped skipped')
    ..writeln()
    ..writeln('| Result | Test | Duration | Details |')
    ..writeln('|---|---|---:|---|');

  for (final test in scenarios) {
    buffer.writeln('| ${_status(test)} | ${_cell(test.name)} | '
        '${(test.durationMs / 1000).toStringAsFixed(2)}s | '
        '${_cell(_detail(test))} |');
  }

  buffer
    ..writeln()
    ..writeln('Generated automatically from the `--file-reporter json` output '
        'by [`tool/e2e_report.dart`](../tool/e2e_report.dart). See '
        '[README](./README.md) for setup and run instructions.')
    ..writeln();

  final out = File(outPath);
  out.parent.createSync(recursive: true);
  out.writeAsStringSync(buffer.toString());

  stdout.writeln('$outPath — ${scenarios.length} scenarios; $passed passed; '
      '$failed failed; $skipped skipped.');

  if (options.htmlPath != null) {
    final html = File(options.htmlPath!);
    html.parent.createSync(recursive: true);
    html.writeAsStringSync(
      _renderHtml(
        scenarios: scenarios,
        success: success,
        runStartUtc: runStartUtc,
        totalMs: totalMs,
        passed: passed,
        failed: failed,
        skipped: skipped,
      ),
    );
    stdout.writeln('${options.htmlPath} — open it in a browser.');
  }
}

/// A self-contained page: `flutter test` ships no HTML report, and a run is easier to read as a page than as a
/// table in a terminal. No CDN, no assets — the file opens offline and can be attached to a report.
String _renderHtml({
  required List<_TestRun> scenarios,
  required bool success,
  required DateTime runStartUtc,
  required int totalMs,
  required int passed,
  required int failed,
  required int skipped,
}) {
  final rows = StringBuffer();
  for (final test in scenarios) {
    final status = _status(test);
    rows.writeln('      <tr data-status="$status">'
        '<td><span class="badge $status">$status</span></td>'
        '<td>${_escapeHtml(test.name)}</td>'
        '<td class="num">${(test.durationMs / 1000).toStringAsFixed(2)}s</td>'
        '<td class="detail">${_escapeHtml(_detail(test))}</td></tr>');
  }

  final seconds = (totalMs / 1000).toStringAsFixed(1);
  final status = success ? 'passed' : 'failed';
  final statusColour = success ? '--pass' : '--fail';

  return '''
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Flutter mobile E2E — $status</title>
<style>
  :root { --pass:#1a7f37; --fail:#cf222e; --skip:#9a6700; --line:#d0d7de; }
  * { box-sizing: border-box; }
  body { margin:0; padding:32px; font:15px/1.5 -apple-system, "Segoe UI", Roboto, sans-serif;
         background:#f6f8fa; color:#1f2328; }
  h1 { margin:0 0 4px; font-size:22px; }
  .sub { opacity:.7; font-size:13px; }
  .pill { display:inline-block; padding:4px 12px; border-radius:999px; font-weight:600; font-size:13px;
          color:#fff; background:var($statusColour); }
  .stats { display:flex; gap:12px; flex-wrap:wrap; margin:24px 0 20px; }
  .card { flex:1 1 130px; border:1px solid var(--line); border-radius:10px; padding:12px 16px; }
  .card b { display:block; font-size:24px; line-height:1.2; }
  .card span { font-size:12px; text-transform:uppercase; letter-spacing:.06em; opacity:.7; }
  .filters { display:flex; gap:8px; margin-bottom:10px; }
  button { border:1px solid var(--line); background:transparent; color:inherit; border-radius:8px;
           padding:5px 12px; font-size:13px; cursor:pointer; }
  button[aria-pressed="true"] { background:#0969da; border-color:#0969da; color:#fff; }
  table { width:100%; border-collapse:collapse; border:1px solid var(--line); border-radius:10px;
          overflow:hidden; background:#fff; }
  th, td { text-align:left; padding:9px 12px; border-bottom:1px solid var(--line); vertical-align:top; }
  th { font-size:12px; text-transform:uppercase; letter-spacing:.05em; color:#57606a; }
  td { background:#fff; }
  tr:last-child td { border-bottom:none; }
  td.num { text-align:right; font-variant-numeric:tabular-nums; white-space:nowrap; }
  td.detail { font-size:13px; opacity:.8; }
  .badge { padding:2px 9px; border-radius:999px; font-size:12px; font-weight:600; color:#fff; }
  .badge.passed { background:var(--pass); } .badge.failed { background:var(--fail); }
  .badge.skipped { background:var(--skip); }
  footer { margin-top:18px; font-size:12px; opacity:.6; }
  @media (prefers-color-scheme: dark) {
    body { background:#0d1117; color:#e6edf3; }
    .card, button { background:#161b22; border-color:#30363d; }
    table { background:#161b22; color:#e6edf3; border-color:#30363d; }
    th { background:#1c2128; color:#9aa4b2; }
    td { background:#161b22; }
    tr:hover td { background:#1f2630; }
  }
</style>
</head>
<body>
  <h1>Flutter mobile E2E</h1>
  <div class="sub">${runStartUtc.toIso8601String()} · Dart ${_dartVersion()} (${Platform.operatingSystem})</div>
  <p><span class="pill">$status</span></p>

  <section class="stats">
    <div class="card"><span>Scenarios</span><b>${scenarios.length}</b></div>
    <div class="card"><span>Passed</span><b>$passed</b></div>
    <div class="card"><span>Failed</span><b>$failed</b></div>
    <div class="card"><span>Skipped</span><b>$skipped</b></div>
    <div class="card"><span>Wall time</span><b>${seconds}s</b></div>
  </section>

  <div class="filters">
    <button data-filter="all" aria-pressed="true">All</button>
    <button data-filter="passed" aria-pressed="false">Passed</button>
    <button data-filter="failed" aria-pressed="false">Failed</button>
    <button data-filter="skipped" aria-pressed="false">Skipped</button>
  </div>

  <table>
    <thead><tr><th>Result</th><th>Test</th><th class="num">Duration</th><th>Details</th></tr></thead>
    <tbody>
$rows    </tbody>
  </table>

  <footer>Generated from <code>flutter test --file-reporter json</code> by <code>app/tool/e2e_report.dart</code>.</footer>

  <script>
    for (const button of document.querySelectorAll('button[data-filter]')) {
      button.addEventListener('click', () => {
        const filter = button.dataset.filter;
        for (const other of document.querySelectorAll('button[data-filter]')) {
          other.setAttribute('aria-pressed', String(other === button));
        }
        for (const row of document.querySelectorAll('tbody tr')) {
          row.hidden = filter !== 'all' && row.dataset.status !== filter;
        }
      });
    }
  </script>
</body>
</html>
''';
}

String _escapeHtml(String value) => value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');

/// How one scenario is called in the report.
String _status(_TestRun test) {
  if (test.skipped) return 'skipped';
  if (test.result == 'success') return 'passed';
  return 'failed';
}

/// What goes in the Details column: the reason a scenario did not pass, trimmed to one cell.
String _detail(_TestRun test) {
  if (test.skipped) return 'skipped';
  if (_status(test) != 'failed') return '';

  final error = test.error ?? '';
  if (error.isEmpty) return 'see the console output';

  final firstLine = error.split('\n').first.trim();
  return firstLine.length <= 160
      ? firstLine
      : '${firstLine.substring(0, 157)}...';
}

/// A Markdown table cell cannot contain a raw pipe or a newline.
String _cell(String value) =>
    value.replaceAll('|', r'\|').replaceAll('\n', ' ').trim();

String _dartVersion() => Platform.version.split(' ').first;

({String jsonPath, String outPath, String? htmlPath}) _parseArgs(
  List<String> args,
) {
  final positional = <String>[];
  var out = 'integration_test/TEST-RESULTS.md';
  String? html;

  for (var index = 0; index < args.length; index++) {
    if (args[index] == '--out' && index + 1 < args.length) {
      out = args[++index];
    } else if (args[index] == '--html' && index + 1 < args.length) {
      html = args[++index];
    } else {
      positional.add(args[index]);
    }
  }

  if (positional.isEmpty) {
    stderr.writeln('Usage: dart run tool/e2e_report.dart <report.json> '
        '[--out integration_test/TEST-RESULTS.md] [--html build/e2e/report.html]');
    exit(64);
  }

  return (jsonPath: positional.first, outPath: out, htmlPath: html);
}

class _TestRun {
  _TestRun({required this.name, required this.startedAtMs});

  final String name;
  final int startedAtMs;

  int durationMs = 0;
  String result = 'error';
  bool skipped = false;
  bool hidden = false;
  String? error;
}
