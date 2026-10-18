import 'problem.dart';

/// One loaded page of a paged list, plus the request that produced it.
///
/// The request travels with the rows for the same reason the rows do: a pager that has to remember what it asked
/// for is a pager that can render page 3 of a filter the user has already cleared. `loading` shows a spinner only
/// where the data is, so the screen does not blink between pages.
///
/// A null `PagedState` in a provider means "never read", which is a different state from "read and empty" — the
/// first shows a spinner, the second says there is nothing.
class PagedState<T> {
  /// Creates a page.
  const PagedState({
    required this.items,
    required this.total,
    required this.skip,
    required this.take,
    required this.search,
    this.isLoading = false,
    this.failure,
  });

  /// Rows on this page.
  final List<T> items;

  /// How many rows match the same filter.
  final int total;

  /// Rows skipped to reach this page.
  final int skip;

  /// Page size this page was asked for.
  final int take;

  /// The filter that produced it.
  final String search;

  /// True while a request for this page is in flight.
  final bool isLoading;

  /// The last failure, kept alongside the rows so a failed page still shows what it had.
  final AppFailure? failure;

  /// 1-based index of the first row, or 0 when there are none.
  int get firstRow => items.isEmpty ? 0 : skip + 1;

  /// 1-based index of the last row.
  int get lastRow => items.isEmpty ? 0 : skip + items.length;

  /// True when a previous page exists.
  bool get hasPrevious => skip > 0;

  /// True when a further page exists.
  bool get hasNext => skip + items.length < total;

  /// The same page, marked as loading: a page turn keeps the rows that are already on screen.
  PagedState<T> asLoading() => copyWith(isLoading: true, failure: null, clearFailure: true);

  /// A copy with the given members replaced.
  PagedState<T> copyWith({
    List<T>? items,
    int? total,
    int? skip,
    int? take,
    String? search,
    bool? isLoading,
    AppFailure? failure,
    bool clearFailure = false,
  }) => PagedState<T>(
    items: items ?? this.items,
    total: total ?? this.total,
    skip: skip ?? this.skip,
    take: take ?? this.take,
    search: search ?? this.search,
    isLoading: isLoading ?? this.isLoading,
    failure: clearFailure ? null : (failure ?? this.failure),
  );
}

/// Rows per page for the administrative lists.
///
/// Tighter than the server's own default of 50: this is a phone surface, and a table of 50 rows on a 6-inch screen
/// is a scroll, not a page.
const int adminPageSize = 20;
