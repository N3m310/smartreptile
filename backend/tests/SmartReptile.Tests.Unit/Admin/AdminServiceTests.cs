using FluentAssertions;
using SmartReptile.Application.Admin;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Tests.Unit.Application;
using SmartReptile.Tests.Unit.Devices;

namespace SmartReptile.Tests.Unit.Admin;

/// <summary>
/// The administrator's dashboard (TASKS 2026-10-14, item 13). What the service owns is the paging contract and the
/// description of the two families, so that is what is asserted; the projections themselves are the store's, and the
/// integration suite reads them against a real database.
/// </summary>
public class AdminServiceTests
{
    private readonly FakeAdminStore _store = new();
    private readonly FakeDeviceSessionRegistry _sessions = new();
    private readonly TestClock _clock = new() { UtcNow = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero) };
    private readonly AdminService _admin;

    public AdminServiceTests() => _admin = new AdminService(_store, _sessions, _clock);

    // ---- overview -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_overview_describes_both_families_with_their_metrics_and_open_counts()
    {
        _store.Counts = Counts() with { AlertsOpenEnclosure = 3, AlertsOpenSpecies = 2 };

        var overview = await _admin.OverviewAsync(CancellationToken.None);

        overview.Families.Select(family => family.Key).Should().Equal("enclosure", "species");

        var enclosure = overview.Families[0];
        enclosure.Metrics.Should().BeEquivalentTo("humidityPct", "lightLux", "batteryPct", "rssiDbm");
        enclosure.AlertsOpen.Should().Be(3);

        var species = overview.Families[1];
        species.Metrics.Should().BeEquivalentTo("tempC", "uvIndex", "surfaceTempC");
        species.AlertsOpen.Should().Be(2);
    }

    [Fact]
    public async Task The_overview_carries_the_counts_and_every_account()
    {
        _store.Counts = Counts();
        _store.Rollups = [Rollup("alice"), Rollup("bob")];

        var overview = await _admin.OverviewAsync(CancellationToken.None);

        overview.Counts.Terrariums.Should().Be(4);
        overview.Accounts.Select(account => account.Username).Should().Equal("alice", "bob");
    }

    // ---- paging -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_caller_that_names_no_page_gets_the_documented_default()
    {
        await _admin.ListTerrariumsAsync(search: null, skip: null, take: null, CancellationToken.None);

        _store.TerrariumPage.Should().Be((0, AdminService.DefaultPageSize));
    }

    [Theory]
    [InlineData(5_000, AdminService.MaxPageSize)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    public async Task A_page_outside_the_limits_is_clamped_rather_than_refused(int asked, int expected)
    {
        await _admin.ListTerrariumsAsync(search: null, skip: null, take: asked, CancellationToken.None);

        _store.TerrariumPage.Take.Should().Be(expected);
    }

    [Fact]
    public async Task A_negative_skip_starts_at_the_first_row()
    {
        await _admin.ListTerrariumsAsync(search: null, skip: -10, take: 5, CancellationToken.None);

        _store.TerrariumPage.Should().Be((0, 5));
    }

    [Fact]
    public async Task The_total_is_counted_with_the_same_search_as_the_page()
    {
        // A total that ignored the filter would make the pager offer pages that are empty.
        _store.TerrariumTotal = 7;

        var page = await _admin.ListTerrariumsAsync("  rop  ", 0, 10, CancellationToken.None);

        page.Total.Should().Be(7);
        page.Items.Should().HaveCount(1);
        _store.TerrariumSearches.Should().OnlyContain(search => search == "rop");
    }

    [Fact]
    public async Task A_blank_search_is_no_filter()
    {
        await _admin.ListTerrariumsAsync("   ", 0, 10, CancellationToken.None);

        _store.TerrariumSearches.Should().OnlyContain(search => string.IsNullOrEmpty(search));
    }

    [Fact]
    public async Task The_page_reports_the_paging_it_used()
    {
        var page = await _admin.ListTerrariumsAsync(search: null, skip: 20, take: 10, CancellationToken.None);

        page.Skip.Should().Be(20);
        page.Take.Should().Be(10);
    }

    [Fact]
    public async Task Boards_page_the_same_way_enclosures_do()
    {
        _store.DeviceTotal = 3;

        var page = await _admin.ListDevicesAsync("sr-", 10, 5, CancellationToken.None);

        page.Total.Should().Be(3);
        _store.DevicePage.Should().Be((10, 5));
        _store.DeviceSearches.Should().OnlyContain(search => search == "sr-");
    }

    // ---- the two alert filters ----------------------------------------------------------------------

    [Fact]
    public async Task The_alert_list_and_its_total_use_the_same_two_filters()
    {
        _store.AlertTotal = 12;

        var page = await _admin.ListAlertsAsync(AlertFamily.Species, AlertState.Open, 0, 50, CancellationToken.None);

        page.Total.Should().Be(12);
        _store.AlertFilters.Should().Equal(
            (AlertFamily.Species, AlertState.Open),
            (AlertFamily.Species, AlertState.Open));
    }

    [Fact]
    public async Task An_absent_family_means_both()
    {
        await _admin.ListAlertsAsync(family: null, state: null, skip: null, take: null, CancellationToken.None);

        _store.AlertFilters.Should().Equal((null, null), (null, null));
    }

    // ---- delete -------------------------------------------------------------------------------------

    [Fact]
    public async Task Deleting_an_enclosure_disconnects_the_boards_it_released()
    {
        var terrariumId = Guid.NewGuid();
        _store.Deletion = new AdminTerrariumDeletion(terrariumId, Guid.NewGuid(), ["sr-000001", "sr-000002"]);

        var deleted = await _admin.DeleteTerrariumAsync(
            terrariumId,
            Guid.NewGuid(),
            AuditActor.Unknown,
            CancellationToken.None);

        deleted.Should().BeTrue();

        // Kicked after the commit, one per board the delete released — the row is what the delete is, the session is
        // only its visible half.
        _sessions.Kicks.Select(kick => kick.DeviceId).Should().Equal("sr-000001", "sr-000002");
        _sessions.Kicks.Should().OnlyContain(kick => kick.Reason == "terrarium_deleted");
        _store.DeletedAt.Should().Be(_clock.UtcNow, "the audit row and the delete share one instant");
    }

    [Fact]
    public async Task An_unknown_enclosure_is_not_deleted_and_closes_no_session()
    {
        _store.Deletion = null;

        var deleted = await _admin.DeleteTerrariumAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AuditActor.Unknown,
            CancellationToken.None);

        deleted.Should().BeFalse();
        _sessions.Kicks.Should().BeEmpty();
    }

    /// <summary>Deployment-wide figures with the two families distinct, so a mix-up is visible.</summary>
    private static AdminCounts Counts() => new(
        Accounts: 2,
        AccountsActive: 1,
        Terrariums: 4,
        TerrariumsWithDevice: 3,
        Devices: 3,
        DevicesOnline: 1,
        AlertsOpen: 5,
        AlertsOpenEnclosure: 3,
        AlertsOpenSpecies: 2,
        AlertsCritical: 1,
        SpeciesProfiles: 6);

    /// <summary>One account's holdings, as the fake hands them back.</summary>
    private static AdminAccountRollup Rollup(string username) => new(
        Guid.NewGuid(),
        username,
        $"{username}@example.test",
        "Owner",
        true,
        Terrariums: 1,
        Devices: 1,
        AlertsOpen: 0,
        DateTimeOffset.UnixEpoch,
        LastLoginAt: null);

    /// <summary>
    /// The store the service is given. It records what it was asked rather than filtering anything, because the
    /// filter is SQL and belongs to the integration suite; what is checked here is that the service asks both the
    /// page and the count for the same thing.
    /// </summary>
    private sealed class FakeAdminStore : IAdminStore
    {
        public AdminCounts Counts { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        public IReadOnlyList<AdminAccountRollup> Rollups { get; set; } = [Rollup("admin")];

        public int TerrariumTotal { get; set; } = 1;

        public int DeviceTotal { get; set; }

        public int AlertTotal { get; set; }

        public (int Skip, int Take) TerrariumPage { get; private set; }

        public (int Skip, int Take) DevicePage { get; private set; }

        public List<string?> TerrariumSearches { get; } = [];

        public List<string?> DeviceSearches { get; } = [];

        public List<(AlertFamily? Family, AlertState? State)> AlertFilters { get; } = [];

        public Task<AdminCounts> CountAsync(CancellationToken cancellationToken) => Task.FromResult(Counts);

        public Task<IReadOnlyList<AdminAccountRollup>> ListAccountRollupsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Rollups);

        public Task<IReadOnlyList<AdminTerrariumView>> ListTerrariumsAsync(
            string? search,
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            TerrariumSearches.Add(search);
            TerrariumPage = (skip, take);

            return Task.FromResult<IReadOnlyList<AdminTerrariumView>>([Terrarium(search)]);
        }

        public Task<int> CountTerrariumsAsync(string? search, CancellationToken cancellationToken)
        {
            TerrariumSearches.Add(search);

            return Task.FromResult(TerrariumTotal);
        }

        public Task<IReadOnlyList<AdminDeviceView>> ListDevicesAsync(
            string? search,
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            DeviceSearches.Add(search);
            DevicePage = (skip, take);

            return Task.FromResult<IReadOnlyList<AdminDeviceView>>([Device()]);
        }

        public Task<int> CountDevicesAsync(string? search, CancellationToken cancellationToken)
        {
            DeviceSearches.Add(search);

            return Task.FromResult(DeviceTotal);
        }

        public Task<IReadOnlyList<AdminAlertView>> ListAlertsAsync(
            AlertFamily? family,
            AlertState? state,
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            AlertFilters.Add((family, state));

            return Task.FromResult<IReadOnlyList<AdminAlertView>>([Alert()]);
        }

        public Task<int> CountAlertsAsync(
            AlertFamily? family,
            AlertState? state,
            CancellationToken cancellationToken)
        {
            AlertFilters.Add((family, state));

            return Task.FromResult(AlertTotal);
        }

        /// <summary>What the store answers for a delete; null stands for "no such enclosure".</summary>
        public AdminTerrariumDeletion? Deletion { get; set; }

        /// <summary>When the delete was asked to happen, so the clock can be asserted.</summary>
        public DateTimeOffset? DeletedAt { get; private set; }

        public Task<AdminTerrariumDeletion?> DeleteTerrariumAsync(
            Guid terrariumId,
            Guid actingUserId,
            AuditActor actor,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken)
        {
            DeletedAt = occurredAt;

            return Task.FromResult(Deletion);
        }

        private static AdminTerrariumView Terrarium(string? search) => new(
            Guid.NewGuid(),
            search ?? "Enclosure",
            Guid.NewGuid(),
            "alice",
            Guid.NewGuid(),
            "Leopard gecko",
            "Active",
            DevicePublicId: "sr-1a2b3c",
            DeviceStatus: "Online",
            DeviceLastSeenAt: null,
            OpenAlerts: 1,
            DateTimeOffset.UnixEpoch);

        private static AdminDeviceView Device() => new(
            Guid.NewGuid(),
            "sr-1a2b3c",
            "Node",
            "Online",
            OwnerUsername: "alice",
            TerrariumId: Guid.NewGuid(),
            TerrariumName: "Enclosure",
            FirmwareVersion: "1.2.0",
            LastSeenAt: null,
            SignalStrengthDbm: -60,
            BatteryPct: 88m);

        private static AdminAlertView Alert() => new(
            1,
            "species",
            "Warning",
            "Open",
            "Threshold",
            Metric: "tempC",
            DeviceId: "sr-1a2b3c",
            TerrariumId: Guid.NewGuid(),
            TerrariumName: "Enclosure",
            OwnerUsername: "alice",
            DateTimeOffset.UnixEpoch,
            ResolvedAt: null);
    }
}
