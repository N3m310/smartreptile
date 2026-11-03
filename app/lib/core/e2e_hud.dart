/// On-device progress banner for the E2E suites (`docs/04-quality/05-e2e-ai-testing-plan.md` §8).
///
/// `flutter test integration_test/...` drives the real app on an emulator, but nothing on the phone says which case
/// is running or what it is waiting for — the screen just moves on its own, which is indistinguishable from "nothing
/// is happening". This is that missing cue: a channel the suites publish to ([E2eHud]) and a banner the app draws
/// over whatever screen is showing ([E2eHudOverlay]).
///
/// Inert in a normal run: nothing publishes, so [E2eHud.status] stays `null` and the overlay returns its child
/// untouched. It costs one `ValueListenableBuilder`, and no pixels.
///
/// The banner draws its text with a [CustomPainter] rather than `Text` widgets on purpose: the suites assert with
/// `find.text`/`find.textContaining`, and a `Text` overlay would add matches of its own to those finders.
library;

import 'dart:async';

import 'package:flutter/material.dart';

/// How a published case ended, or that it is still running.
enum E2eOutcome { running, passed, failed }

/// What the banner shows: which suite and case, its current step, and when it started.
@immutable
class E2eRunStatus {
  const E2eRunStatus({
    required this.suite,
    required this.testCase,
    required this.step,
    required this.startedAt,
    required this.outcome,
  });

  final String suite;
  final String testCase;
  final String step;
  final DateTime startedAt;
  final E2eOutcome outcome;

  E2eRunStatus copyWith({String? step, E2eOutcome? outcome}) => E2eRunStatus(
    suite: suite,
    testCase: testCase,
    step: step ?? this.step,
    startedAt: startedAt,
    outcome: outcome ?? this.outcome,
  );
}

/// The channel an integration test publishes to. The app only reads it.
class E2eHud {
  E2eHud._();

  /// `null` until a case starts, which is what keeps the banner out of a production or manual run.
  static final ValueNotifier<E2eRunStatus?> status =
      ValueNotifier<E2eRunStatus?>(null);

  /// Announces a case. Call it before `app.main()`, or right after: the banner appears with the first frame.
  static void begin({required String suite, required String testCase}) {
    status.value = E2eRunStatus(
      suite: suite,
      testCase: testCase,
      step: 'starting the app',
      startedAt: DateTime.now(),
      outcome: E2eOutcome.running,
    );
  }

  /// Names the action in flight, so the phone shows the tap that is about to happen rather than the result of it.
  ///
  /// Ignored when no case has begun: a plain `flutter test` run of the same widget test stays silent.
  static void step(String text) {
    final current = status.value;
    if (current == null) return;
    status.value = current.copyWith(step: text);
  }

  /// Closes the case and turns the banner green or red.
  static void finish({required bool passed}) {
    final current = status.value;
    if (current == null) return;
    status.value = current.copyWith(
      outcome: passed ? E2eOutcome.passed : E2eOutcome.failed,
    );
  }
}

/// Draws [E2eHud.status] over the app. A no-op while nothing is published.
class E2eHudOverlay extends StatelessWidget {
  const E2eHudOverlay({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return ValueListenableBuilder<E2eRunStatus?>(
      valueListenable: E2eHud.status,
      child: child,
      builder: (context, status, child) {
        if (status == null) return child!;
        return Stack(
          children: [
            child!,
            Positioned(
              top: 0,
              left: 0,
              right: 0,
              // The suites tap through semantics finders, so the banner must never take a pointer event.
              child: IgnorePointer(child: _E2eBanner(status: status)),
            ),
          ],
        );
      },
    );
  }
}

const TextStyle _titleStyle = TextStyle(
  fontSize: 12,
  height: 1.25,
  fontWeight: FontWeight.w800,
  letterSpacing: 1.2,
);
const TextStyle _caseStyle = TextStyle(
  fontSize: 13,
  height: 1.25,
  fontWeight: FontWeight.w600,
  color: Color(0xFFF2F6F8),
);
const TextStyle _stepStyle = TextStyle(
  fontSize: 11.5,
  height: 1.3,
  color: Color(0xFFA8B7C2),
);

class _E2eBanner extends StatefulWidget {
  const _E2eBanner({required this.status});

  final E2eRunStatus status;

  @override
  State<_E2eBanner> createState() => _E2eBannerState();
}

class _E2eBannerState extends State<_E2eBanner> {
  Timer? _ticker;
  bool _blink = true;

  @override
  void initState() {
    super.initState();
    _syncTicker();
  }

  @override
  void didUpdateWidget(_E2eBanner oldWidget) {
    super.didUpdateWidget(oldWidget);
    // The banner is reused between cases: a case that ended stops the ticker, and the next one has to start it
    // again, or its clock would sit frozen at the previous case's last second.
    _syncTicker();
  }

  void _syncTicker() {
    if (widget.status.outcome != E2eOutcome.running) {
      _ticker?.cancel();
      _ticker = null;
      _blink = true;
      return;
    }
    // One timer for both jobs: the elapsed clock has to move, and a still dot reads as "frozen".
    _ticker ??= Timer.periodic(const Duration(milliseconds: 500), (_) {
      if (mounted) setState(() => _blink = !_blink);
    });
  }

  @override
  void dispose() {
    _ticker?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final elapsed = _formatElapsed(
      DateTime.now().difference(widget.status.startedAt),
    );
    final width =
        MediaQuery.sizeOf(context).width -
        _E2eBannerPainter.margin * 2 -
        _E2eBannerPainter.padding.horizontal;
    final casePainter = _layout(widget.status.testCase, _caseStyle, width);
    final stepPainter = _layout(widget.status.step, _stepStyle, width);
    final height =
        _E2eBannerPainter.padding.vertical +
        _E2eBannerPainter.titleHeight +
        _E2eBannerPainter.gapAfterTitle +
        casePainter.height +
        _E2eBannerPainter.gapAfterCase +
        stepPainter.height;

    return SafeArea(
      bottom: false,
      child: Padding(
        padding: const EdgeInsets.all(_E2eBannerPainter.margin),
        child: SizedBox(
          height: height,
          child: CustomPaint(
            painter: _E2eBannerPainter(
              status: widget.status,
              elapsed: elapsed,
              blink: _blink,
            ),
          ),
        ),
      ),
    );
  }
}

TextPainter _layout(String text, TextStyle style, double maxWidth) =>
    TextPainter(
      text: TextSpan(text: text, style: style),
      textDirection: TextDirection.ltr,
      maxLines: 2,
      ellipsis: '\u2026',
    )..layout(maxWidth: maxWidth);

String _formatElapsed(Duration elapsed) {
  final minutes = elapsed.inMinutes;
  final seconds = elapsed.inSeconds.remainder(60).toString().padLeft(2, '0');
  return '$minutes:$seconds';
}

class _E2eBannerPainter extends CustomPainter {
  const _E2eBannerPainter({
    required this.status,
    required this.elapsed,
    required this.blink,
  });

  final E2eRunStatus status;
  final String elapsed;
  final bool blink;

  static const double margin = 6;
  static const EdgeInsets padding = EdgeInsets.fromLTRB(12, 8, 12, 11);
  static const double titleHeight = 15;
  static const double gapAfterTitle = 4;
  static const double gapAfterCase = 2;

  static const Color _background = Color(0xF2131A20);
  static const Color _titleColor = Color(0xFFFFC400);
  static const Color _passedColor = Color(0xFF56D364);
  static const Color _failedColor = Color(0xFFFF5A5F);

  @override
  void paint(Canvas canvas, Size size) {
    final color = switch (status.outcome) {
      E2eOutcome.running => _titleColor,
      E2eOutcome.passed => _passedColor,
      E2eOutcome.failed => _failedColor,
    };
    final panel = RRect.fromRectAndRadius(
      (Offset.zero & size).deflate(0.75),
      const Radius.circular(10),
    );
    canvas.drawRRect(panel, Paint()..color = _background);
    canvas.drawRRect(
      panel,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1.5
        ..color = color,
    );

    const left = 12.0;
    final right = size.width - padding.right;
    final dot = Offset(left + 4.5, padding.top + titleHeight / 2);
    canvas.drawCircle(
      dot,
      4.5,
      Paint()..color = color.withValues(alpha: blink ? 1 : 0.28),
    );

    final outcomeLabel = switch (status.outcome) {
      E2eOutcome.running => 'E2E RUNNING',
      E2eOutcome.passed => 'E2E PASSED',
      E2eOutcome.failed => 'E2E FAILED',
    };
    const clockWidth = 44.0;
    _paint(
      canvas,
      '$outcomeLabel  \u00b7  ${status.suite.toUpperCase()}',
      Offset(left + 14, padding.top),
      _titleStyle.copyWith(color: color),
      right - left - 14 - clockWidth,
      1,
    );
    _paint(
      canvas,
      elapsed,
      Offset(right - clockWidth, padding.top),
      _titleStyle.copyWith(color: color.withValues(alpha: 0.85)),
      clockWidth,
      1,
      align: TextAlign.right,
    );

    var y = padding.top + titleHeight + gapAfterTitle;
    final casePainter = _paint(
      canvas,
      status.testCase,
      Offset(left, y),
      _caseStyle,
      right - left,
      2,
    );
    y += casePainter + gapAfterCase;
    _paint(canvas, status.step, Offset(left, y), _stepStyle, right - left, 2);
  }

  /// Paints one run of text and returns the height it used.
  double _paint(
    Canvas canvas,
    String text,
    Offset at,
    TextStyle style,
    double maxWidth,
    int maxLines, {
    TextAlign align = TextAlign.left,
  }) {
    final painter = TextPainter(
      text: TextSpan(text: text, style: style),
      textDirection: TextDirection.ltr,
      maxLines: maxLines,
      ellipsis: '\u2026',
      textAlign: align,
    )..layout(minWidth: maxWidth, maxWidth: maxWidth);
    painter.paint(canvas, at);
    return painter.height;
  }

  @override
  bool shouldRepaint(_E2eBannerPainter oldDelegate) =>
      oldDelegate.status != status ||
      oldDelegate.elapsed != elapsed ||
      oldDelegate.blink != blink;
}
