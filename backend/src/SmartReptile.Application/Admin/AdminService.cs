using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;

namespace SmartReptile.Application.Admin;

/// <summary>
/// The administrator's dashboard (TASKS 2026-10-14, item 13). Every read here is cross-account, which is why the
/// service is reached only through routes naming <c>Permissions.DataReadAny</c>.
/// </summary>
/// <remarks>
/// <b>Why there is a service at all when the store already returns the rendered shapes.</b> Two things belong
/// outside a query: paging (the clamp, and the total that has to be counted with the same filter), and the two
/// families' description — which metrics belong to each, and how many of their alerts are open. The second is what
/// lets the dashboard label its two tabs from the same answer that fills them, rather than from a list of metric keys
/// the JavaScript would have to carry (the mistake DOC_CHANGES_2026-10-03 §1 records for the web client).
/// </remarks>
public sealed class AdminService(IAdminStore store)
{
    /// <summary>Rows a page asks for when the caller names none.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Largest page the dashboard may ask for. Beyond this the answer stops being a screen.</summary>
    public const int MaxPageSize = 200;

    /// <summary>The header figures and the get-all table.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AdminOverviewView> OverviewAsync(CancellationToken cancellationToken)
    {
        var counts = await store.CountAsync(cancellationToken).ConfigureAwait(false);
        var accounts = await store.ListAccountRollupsAsync(cancellationToken).ConfigureAwait(false);

        // The families are described from the metric dictionary rather than from a second table: the dictionary is
        // where each metric's family is written down, so a metric added there appears in the right tab on the next
        // request with nothing else to change.
        var families = AlertFamilies.All
            .Select(family => new AdminFamilyView(
                AlertFamilies.ApiKey(family),
                [.. MetricDictionary.All
                    .Where(definition => definition.Family == family)
                    .Select(definition => definition.ApiKey)
                    .OrderBy(key => key, StringComparer.Ordinal)],
                OpenAlertsIn(counts, family)))
            .ToArray();

        return new AdminOverviewView(counts, accounts, families);
    }

    /// <summary>Every account's enclosures, newest first.</summary>
    /// <param name="search">Matches enclosure name or owner username, case-insensitively.</param>
    /// <param name="skip">Rows to skip.</param>
    /// <param name="take">Rows to return, clamped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AdminPage<AdminTerrariumView>> ListTerrariumsAsync(
        string? search,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var page = Page(skip, take);

        // Counted before the page is read, and with the same filter: a total that ignored the search would make the
        // pager offer pages that are empty.
        var total = await store
            .CountTerrariumsAsync(Normalise(search), cancellationToken)
            .ConfigureAwait(false);

        var items = await store
            .ListTerrariumsAsync(Normalise(search), page.Skip, page.Take, cancellationToken)
            .ConfigureAwait(false);

        return new AdminPage<AdminTerrariumView>(items, total, page.Skip, page.Take);
    }

    /// <summary>Every board in the deployment, last seen first.</summary>
    /// <param name="search">Matches public id, device name or owner username, case-insensitively.</param>
    /// <param name="skip">Rows to skip.</param>
    /// <param name="take">Rows to return, clamped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AdminPage<AdminDeviceView>> ListDevicesAsync(
        string? search,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var page = Page(skip, take);

        var total = await store
            .CountDevicesAsync(Normalise(search), cancellationToken)
            .ConfigureAwait(false);

        var items = await store
            .ListDevicesAsync(Normalise(search), page.Skip, page.Take, cancellationToken)
            .ConfigureAwait(false);

        return new AdminPage<AdminDeviceView>(items, total, page.Skip, page.Take);
    }

    /// <summary>Every account's alerts, newest first.</summary>
    /// <param name="family">Only this family, or null for both.</param>
    /// <param name="state">Only this state, or null for every state.</param>
    /// <param name="skip">Rows to skip.</param>
    /// <param name="take">Rows to return, clamped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AdminPage<AdminAlertView>> ListAlertsAsync(
        AlertFamily? family,
        AlertState? state,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var page = Page(skip, take);

        var total = await store
            .CountAlertsAsync(family, state, cancellationToken)
            .ConfigureAwait(false);

        var items = await store
            .ListAlertsAsync(family, state, page.Skip, page.Take, cancellationToken)
            .ConfigureAwait(false);

        return new AdminPage<AdminAlertView>(items, total, page.Skip, page.Take);
    }

    /// <summary>Open alerts of one family, out of the header's figures.</summary>
    private static int OpenAlertsIn(AdminCounts counts, AlertFamily family) => family switch
    {
        AlertFamily.Enclosure => counts.AlertsOpenEnclosure,
        AlertFamily.Species => counts.AlertsOpenSpecies,
        _ => 0,
    };

    /// <summary>A blank search means "no filter" rather than "match the empty string".</summary>
    private static string? Normalise(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : search.Trim();

    /// <summary>Clamps the paging the caller asked for.</summary>
    private static (int Skip, int Take) Page(int? skip, int? take) =>
        (Math.Max(skip ?? 0, 0), Math.Clamp(take ?? DefaultPageSize, 1, MaxPageSize));
}
