using FluentAssertions;
using SmartReptile.Application.Auditing;
using SmartReptile.Domain.Auditing;

namespace SmartReptile.Tests.Unit.Application.Auditing;

/// <summary>
/// Task 3.7 — the audit query. Covers the paging contract (<c>07-appendices/03</c> §4.5's cursor convention), the
/// refusals a caller can provoke, and the wire shape; the ownership scoping is the adapter's and is covered against
/// SQL Server in the integration project.
/// </summary>
public class AuditServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeAuditStore _store = new();
    private readonly AuditService _service;
    private readonly Guid _user = Guid.NewGuid();

    public AuditServiceTests() => _service = new AuditService(_store);

    private static DateTimeOffset At(int minutes) => Now.AddMinutes(minutes);

    [Fact]
    public async Task List_is_newest_first()
    {
        _store
            .Add(1, AuditAction.DeviceClaimed, At(0))
            .Add(2, AuditAction.AlertAcknowledged, At(1))
            .Add(3, AuditAction.AlertResolved, At(2));

        var outcome = await _service.ListAsync(_user, new AuditQuery(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Page!.Items.Select(item => item.Id).Should().Equal(3, 2, 1);
    }

    [Fact]
    public async Task List_pages_without_repeating_or_skipping_a_row()
    {
        _store
            .Add(1, AuditAction.DeviceClaimed, At(0))
            .Add(2, AuditAction.AlertAcknowledged, At(1))
            .Add(3, AuditAction.AlertResolved, At(2))
            .Add(4, AuditAction.DeviceRevoked, At(3));

        var first = await _service.ListAsync(_user, new AuditQuery(PageSize: 2), CancellationToken.None);

        first.Page!.Items.Select(item => item.Id).Should().Equal(4, 3);
        first.Page.NextCursor.Should().NotBeNull("there are rows behind this page");

        var second = await _service.ListAsync(
            _user,
            new AuditQuery(PageSize: 2, Cursor: first.Page.NextCursor),
            CancellationToken.None);

        second.Page!.Items.Select(item => item.Id).Should().Equal(2, 1);
        second.Page.NextCursor.Should().BeNull("the last page carries no next position");
    }

    [Fact]
    public async Task List_breaks_a_shared_instant_by_identity_so_the_cursor_cannot_loop()
    {
        // Two rows recorded in the same millisecond: without the identity as a tie-breaker the cursor would either
        // repeat one or skip the other, depending on which the provider happened to return first.
        _store
            .Add(1, AuditAction.DeviceClaimed, Now)
            .Add(2, AuditAction.AlertAcknowledged, Now)
            .Add(3, AuditAction.AlertResolved, Now);

        var first = await _service.ListAsync(_user, new AuditQuery(PageSize: 2), CancellationToken.None);
        first.Page!.Items.Select(item => item.Id).Should().Equal(3, 2);

        var second = await _service.ListAsync(
            _user,
            new AuditQuery(PageSize: 2, Cursor: first.Page.NextCursor),
            CancellationToken.None);

        second.Page!.Items.Select(item => item.Id).Should().Equal(1);
    }

    [Fact]
    public async Task List_asks_for_one_row_more_than_the_page()
    {
        _store.Add(1, AuditAction.DeviceClaimed, At(0));

        await _service.ListAsync(_user, new AuditQuery(PageSize: 5), CancellationToken.None);

        // The lookahead row is how "there is more" is known without a second count query.
        _store.LastTake.Should().Be(6);
    }

    [Fact]
    public async Task List_clamps_the_page_size_to_the_documented_maximum()
    {
        _store.Add(1, AuditAction.DeviceClaimed, At(0));

        await _service.ListAsync(_user, new AuditQuery(PageSize: 10_000), CancellationToken.None);

        _store.LastTake.Should().Be(AuditPaging.MaxPageSize + 1);
    }

    [Fact]
    public async Task List_of_an_empty_trail_is_an_empty_page()
    {
        var outcome = await _service.ListAsync(_user, new AuditQuery(), CancellationToken.None);

        outcome.Page!.Items.Should().BeEmpty();
        outcome.Page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task List_refuses_a_cursor_it_never_issued()
    {
        _store.Add(1, AuditAction.DeviceClaimed, At(0));

        var outcome = await _service.ListAsync(
            _user,
            new AuditQuery(Cursor: "not-a-cursor"),
            CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("invalid_cursor");
        _store.LastTake.Should().Be(0, "a refused request never reaches the store");
    }

    [Fact]
    public async Task List_refuses_a_reversed_window()
    {
        var outcome = await _service.ListAsync(
            _user,
            new AuditQuery(FromUtc: At(10), ToUtc: At(0)),
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("invalid_range");
        _store.LastTake.Should().Be(0);
    }

    [Fact]
    public async Task List_passes_the_filters_through_and_applies_them()
    {
        _store
            .Add(1, AuditAction.DeviceClaimed, At(0), AuditEntity.Device, entityId: "sr-abc123")
            .Add(2, AuditAction.AlertAcknowledged, At(1), AuditEntity.Alert, entityId: "812")
            .Add(3, AuditAction.AlertResolved, At(2), AuditEntity.Alert, entityId: "812")
            .Add(4, AuditAction.MetricSilenced, At(3), AuditEntity.MetricSilence, entityId: "99");

        var byEntity = await _service.ListAsync(
            _user,
            new AuditQuery(Entity: AuditEntity.Alert),
            CancellationToken.None);

        byEntity.Page!.Items.Select(item => item.Id).Should().Equal(3, 2);

        var byEntityId = await _service.ListAsync(
            _user,
            new AuditQuery(EntityId: "sr-abc123"),
            CancellationToken.None);

        byEntityId.Page!.Items.Single().Id.Should().Be(1);

        var window = await _service.ListAsync(
            _user,
            new AuditQuery(FromUtc: At(1), ToUtc: At(2)),
            CancellationToken.None);

        window.Page!.Items.Select(item => item.Id).Should().Equal(3, 2);
    }

    [Fact]
    public async Task List_carries_the_public_device_id_and_the_stored_json()
    {
        _store.Add(
            1,
            AuditAction.AlertResolved,
            At(0),
            AuditEntity.Alert,
            entityId: "812",
            devicePublicId: "sr-abc123",
            actorUserId: _user,
            before: """{"state":"Open"}""",
            after: """{"state":"Resolved"}""");

        var entry = (await _service.ListAsync(_user, new AuditQuery(), CancellationToken.None)).Page!.Items.Single();

        entry.Entity.Should().Be("Alert");
        entry.EntityId.Should().Be("812");
        entry.DeviceId.Should().Be("sr-abc123", "the surrogate key is not what a client can act on");
        entry.ActorUserId.Should().Be(_user);
        entry.Action.Should().Be("alert.resolved");

        // The snapshot travels as the JSON that was stored rather than as a re-shaped schema: the trail is a faithful
        // record and the four entity kinds do not share one shape.
        entry.Before.Should().Be("""{"state":"Open"}""");
        entry.After.Should().Be("""{"state":"Resolved"}""");
    }

    [Fact]
    public void The_cursor_round_trips_and_rejects_what_it_did_not_write()
    {
        var cursor = AuditCursor.Encode(Now, 42);

        AuditCursor.TryDecode(cursor, out var position).Should().BeTrue();
        position.OccurredAt.Should().Be(Now);
        position.Id.Should().Be(42);

        AuditCursor.TryDecode("not-base64!!", out _).Should().BeFalse();
        AuditCursor.TryDecode(null, out _).Should().BeFalse();
        AuditCursor.TryDecode(Convert.ToBase64String("not|a|pair"u8.ToArray()), out _).Should().BeFalse();
    }
}
