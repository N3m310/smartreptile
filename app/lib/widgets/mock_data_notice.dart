import 'package:flutter/material.dart';

import '../l10n/generated/app_localizations.dart';
import '../theme/app_theme.dart';

/// Honesty notice for a screen that renders generated data instead of measurements.
///
/// The web client carries the same idea (`web/src/components/MockDataNotice.tsx`, task 4.14): a screen shows it if
/// and only if it is inventing its numbers, and it goes away when the screen is wired rather than when someone
/// remembers to delete the label. Here it sits in the shell because the whole app is still on the M1 mock models,
/// so the app carries it on every tab; it moves into the screens one at a time as they are wired, which is the
/// point at which each of them stops needing it.
///
/// The copy is in the shared deck (`app/lib/l10n/app_*.arb`) rather than hard-coded Vietnamese, since a bilingual
/// client cannot carry a string only one of its languages can read.
class MockDataNotice extends StatelessWidget {
  /// Creates the notice.
  const MockDataNotice({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.fromLTRB(14, 8, 14, 8),
      decoration: BoxDecoration(
        color: AppColors.statusWarning.withValues(alpha: 0.12),
        border: const Border(bottom: BorderSide(color: AppColors.border)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Icon(
            Icons.science_outlined,
            size: 16,
            color: AppColors.statusWarning,
          ),
          const SizedBox(width: 8),
          Expanded(
            child: Text.rich(
              TextSpan(
                style: const TextStyle(
                  fontSize: 11,
                  height: 1.35,
                  color: AppColors.textMain,
                ),
                children: [
                  TextSpan(
                    text: l10n.appSimulatedTitle,
                    style: const TextStyle(
                      fontWeight: FontWeight.bold,
                      color: AppColors.statusWarning,
                    ),
                  ),
                  TextSpan(text: ' ${l10n.appSimulatedBody}'),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
