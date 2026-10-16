import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/paging.dart';
import '../../core/problem.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../state/session_provider.dart';
import '../../theme/app_theme.dart';

/// The pieces every administration screen is assembled from.
///
/// They live together because they are one visual language: a section is a card, a figure is a tile, a state is a
/// pill. A screen that invented its own would make the deployment view look like a different product from the
/// account list next to it.

/// A titled section of a screen.
class AdminSection extends StatelessWidget {
  /// Creates a section.
  const AdminSection({
    super.key,
    required this.title,
    this.caption,
    this.trailing,
    required this.child,
  });

  /// Section heading.
  final String title;

  /// One line explaining what the section answers, shown under the heading.
  final String? caption;

  /// Optional action aligned with the heading.
  final Widget? trailing;

  /// Section body.
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.only(bottom: 16),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.bgCard,
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  title,
                  style: const TextStyle(
                    fontSize: 15,
                    fontWeight: FontWeight.bold,
                    color: AppColors.textMain,
                  ),
                ),
              ),
              ?trailing,
            ],
          ),
          if (caption != null) ...[
            const SizedBox(height: 4),
            Text(
              caption!,
              style: const TextStyle(
                fontSize: 11,
                height: 1.35,
                color: AppColors.textMuted,
              ),
            ),
          ],
          const SizedBox(height: 12),
          child,
        ],
      ),
    );
  }
}

/// One figure of the dashboard header: a number, what it counts, and an icon.
class AdminCountTile extends StatelessWidget {
  /// Creates a tile.
  const AdminCountTile({
    super.key,
    required this.label,
    required this.value,
    required this.icon,
    required this.color,
  });

  /// What the figure counts.
  final String label;

  /// The figure, already formatted.
  final String value;

  /// Icon shown beside it.
  final IconData icon;

  /// Colour of the icon and of the figure when it is not zero.
  final Color color;

  @override
  Widget build(BuildContext context) {
    final isZero = value == '0';

    return Container(
      width: 156,
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: AppColors.bgMain,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppColors.border),
      ),
      child: Row(
        children: [
          Icon(icon, size: 18, color: isZero ? AppColors.textMuted : color),
          const SizedBox(width: 8),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  value,
                  style: TextStyle(
                    fontSize: 18,
                    fontWeight: FontWeight.w900,
                    color: isZero ? AppColors.textMuted : AppColors.textMain,
                  ),
                ),
                Text(
                  label,
                  style: const TextStyle(
                    fontSize: 10,
                    height: 1.2,
                    color: AppColors.textMuted,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// A short state label — active, disabled, online, critical — never colour alone (NFR-06).
class AdminStatusPill extends StatelessWidget {
  /// Creates a pill.
  const AdminStatusPill({
    super.key,
    required this.label,
    required this.color,
    this.icon,
  });

  /// Text shown.
  final String label;

  /// Colour of the text and of the tint behind it.
  final Color color;

  /// Optional leading icon, so the state is readable without colour.
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.14),
        borderRadius: BorderRadius.circular(20),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            Icon(icon, size: 12, color: color),
            const SizedBox(width: 4),
          ],
          Text(
            label,
            style: TextStyle(
              fontSize: 10,
              fontWeight: FontWeight.bold,
              color: color,
            ),
          ),
        ],
      ),
    );
  }
}

/// One `label: value` line, used wherever a row carries more than a table cell can hold on a phone.
class AdminKeyValue extends StatelessWidget {
  /// Creates a line.
  const AdminKeyValue({
    super.key,
    required this.label,
    required this.value,
    this.valueColor,
    this.monospace = false,
  });

  /// Left-hand label.
  final String label;

  /// Right-hand value.
  final String value;

  /// Optional colour for the value.
  final Color? valueColor;

  /// True for ids, codes and public keys, which read better fixed-width.
  final bool monospace;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 3),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 132,
            child: Text(
              label,
              style: const TextStyle(fontSize: 11, color: AppColors.textMuted),
            ),
          ),
          Expanded(
            child: Text(
              value,
              style: TextStyle(
                fontSize: 11,
                color: valueColor ?? AppColors.textMain,
                fontFamily: monospace ? 'monospace' : null,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// The search box the paged lists share.
class AdminSearchField extends StatefulWidget {
  /// Creates a search field.
  const AdminSearchField({
    super.key,
    required this.hint,
    required this.onSubmitted,
  });

  /// Placeholder text.
  final String hint;

  /// Called with the text once the keeper submits it — typing does not query the server on every keystroke.
  final ValueChanged<String> onSubmitted;

  @override
  State<AdminSearchField> createState() => _AdminSearchFieldState();
}

class _AdminSearchFieldState extends State<AdminSearchField> {
  final TextEditingController _controller = TextEditingController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return TextField(
      controller: _controller,
      textInputAction: TextInputAction.search,
      onSubmitted: widget.onSubmitted,
      decoration: InputDecoration(
        hintText: widget.hint,
        prefixIcon: const Icon(Icons.search, size: 18),
        suffixIcon: _controller.text.isEmpty
            ? null
            : IconButton(
                icon: const Icon(Icons.close, size: 16),
                onPressed: () {
                  _controller.clear();
                  widget.onSubmitted('');
                },
              ),
      ),
    );
  }
}

/// The footer of a paged list: which rows are on screen, and the two buttons that turn the page.
class AdminPager<T> extends StatelessWidget {
  /// Creates a pager.
  const AdminPager({
    super.key,
    required this.state,
    required this.onPrevious,
    required this.onNext,
  });

  /// The page on screen.
  final PagedState<T> state;

  /// Called when the keeper asks for the previous page.
  final VoidCallback onPrevious;

  /// Called when the keeper asks for the next page.
  final VoidCallback onNext;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(
        children: [
          Expanded(
            child: Text(
              state.total == 0
                  ? ''
                  : l10n.adminPagerRange(
                      state.firstRow,
                      state.lastRow,
                      state.total,
                    ),
              style: const TextStyle(fontSize: 11, color: AppColors.textMuted),
            ),
          ),
          IconButton(
            tooltip: l10n.adminPrevious,
            onPressed: state.hasPrevious && !state.isLoading
                ? onPrevious
                : null,
            icon: const Icon(Icons.chevron_left),
          ),
          IconButton(
            tooltip: l10n.adminNext,
            onPressed: state.hasNext && !state.isLoading ? onNext : null,
            icon: const Icon(Icons.chevron_right),
          ),
        ],
      ),
    );
  }
}

/// What a failed read looks like: the reason, its trace id, and a way to try again.
class AdminErrorView extends StatelessWidget {
  /// Creates the view.
  const AdminErrorView({super.key, required this.failure, this.onRetry});

  /// The failure to explain.
  final AppFailure failure;

  /// Called when the keeper asks for another attempt.
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: AppColors.statusDanger.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppColors.statusDanger.withValues(alpha: 0.4)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(
                Icons.error_outline,
                size: 16,
                color: AppColors.statusDanger,
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  l10n.errorGeneric,
                  style: const TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.bold,
                    color: AppColors.textMain,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            failure.detail ?? failure.title ?? failure.code,
            style: const TextStyle(fontSize: 11, color: AppColors.textMuted),
          ),
          if (failure.traceId != null) ...[
            const SizedBox(height: 4),
            Text(
              failure.traceId!,
              style: const TextStyle(
                fontSize: 10,
                fontFamily: 'monospace',
                color: AppColors.textMuted,
              ),
            ),
          ],
          if (onRetry != null) ...[
            const SizedBox(height: 4),
            TextButton(onPressed: onRetry, child: Text(l10n.retry)),
          ],
        ],
      ),
    );
  }
}

/// What an empty list says: a sentence about the filter or about the deployment, never a bare blank.
class AdminEmptyView extends StatelessWidget {
  /// Creates the view.
  const AdminEmptyView({
    super.key,
    required this.message,
    this.icon = Icons.inbox_outlined,
  });

  /// The sentence to show.
  final String message;

  /// Icon above it.
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 24),
      child: Column(
        children: [
          Icon(icon, size: 32, color: AppColors.textMuted),
          const SizedBox(height: 8),
          Text(
            message,
            textAlign: TextAlign.center,
            style: const TextStyle(fontSize: 12, color: AppColors.textMuted),
          ),
        ],
      ),
    );
  }
}

/// A centred spinner for a first read.
class AdminLoading extends StatelessWidget {
  /// Creates the spinner.
  const AdminLoading({super.key});

  @override
  Widget build(BuildContext context) => const Padding(
    padding: EdgeInsets.symmetric(vertical: 32),
    child: Center(
      child: SizedBox(
        width: 22,
        height: 22,
        child: CircularProgressIndicator(strokeWidth: 2),
      ),
    ),
  );
}

/// The permission gate every administration screen sits behind.
///
/// The shell already hides a tab the session may not open, so reaching this is either a stale route (the session
/// ended while a screen was pushed) or a test. Saying which permission is missing beats rendering a screen whose
/// every request would come back `403`.
class AdminGate extends StatelessWidget {
  /// Creates the gate.
  const AdminGate({super.key, required this.permission, required this.child});

  /// The permission the screen needs.
  final String permission;

  /// What to build when it is held.
  final Widget child;

  @override
  Widget build(BuildContext context) {
    if (context.watch<SessionProvider>().hasPermission(permission)) {
      return child;
    }

    return AdminNotPermitted(permission: permission);
  }
}

/// The panel shown when a screen's permission is missing.
class AdminNotPermitted extends StatelessWidget {
  /// Creates the panel.
  const AdminNotPermitted({super.key, this.permission});

  /// The permission that is missing, shown so the refusal is actionable.
  final String? permission;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final isSignedIn = context.watch<SessionProvider>().isSignedIn;

    return Center(
      child: Padding(
        padding: const EdgeInsets.all(28),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            const Icon(
              Icons.lock_outline,
              size: 40,
              color: AppColors.textMuted,
            ),
            const SizedBox(height: 12),
            Text(
              l10n.adminNotPermitted,
              textAlign: TextAlign.center,
              style: const TextStyle(
                fontSize: 14,
                fontWeight: FontWeight.bold,
                color: AppColors.textMain,
              ),
            ),
            const SizedBox(height: 6),
            Text(
              isSignedIn
                  ? l10n.adminNotPermittedHint
                  : l10n.sessionSignInForAdmin,
              textAlign: TextAlign.center,
              style: const TextStyle(fontSize: 11, color: AppColors.textMuted),
            ),
            if (permission != null) ...[
              const SizedBox(height: 10),
              Text(
                permission!,
                style: const TextStyle(
                  fontSize: 11,
                  fontFamily: 'monospace',
                  color: AppColors.textMuted,
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
