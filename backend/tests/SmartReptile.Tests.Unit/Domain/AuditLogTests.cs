using FluentAssertions;
using SmartReptile.Domain.Auditing;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// BR-18.4 — the shape of an audit row. The column widths are asserted here because both client-supplied header
/// values are unbounded, and an over-long one would fail the insert and turn a valid claim into a 500.
/// </summary>
public class AuditLogTests
{
    private static readonly Guid DeviceId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ForDevice_names_the_entity_the_way_the_api_does()
    {
        var userId = Guid.NewGuid();

        var entry = AuditLog.ForDevice(AuditAction.DeviceRevoked, DeviceId, "sr-3f9a2c", userId, AuditActor.Unknown, Now);

        entry.EntityName.Should().Be("Device");
        entry.EntityId.Should().Be("sr-3f9a2c", "a row is read without joining to the entity table");
        entry.Action.Should().Be("device.revoked");
        entry.DeviceId.Should().Be(DeviceId);
        entry.UserId.Should().Be(userId);
        entry.OccurredAt.Should().Be(Now);
        entry.BeforeJson.Should().BeNull();
        entry.IpAddress.Should().Be("unknown");
    }

    [Fact]
    public void ForDevice_truncates_the_client_supplied_headers_to_their_columns()
    {
        var actor = new AuditActor(
            "203.0.113.7",
            new string('a', AuditLog.UserAgentMaxLength + 50),
            new string('b', AuditLog.CorrelationIdMaxLength + 50));

        var entry = AuditLog.ForDevice(AuditAction.DeviceClaimed, DeviceId, "sr-3f9a2c", null, actor, Now);

        entry.UserAgent.Should().HaveLength(AuditLog.UserAgentMaxLength);
        entry.CorrelationId.Should().HaveLength(AuditLog.CorrelationIdMaxLength);
    }

    [Fact]
    public void ForDevice_leaves_values_that_fit_alone()
    {
        var actor = new AuditActor("203.0.113.7", "curl/8.5.0", "corr-1");

        var entry = AuditLog.ForDevice(AuditAction.DeviceClaimed, DeviceId, "sr-3f9a2c", null, actor, Now);

        entry.UserAgent.Should().Be("curl/8.5.0");
        entry.CorrelationId.Should().Be("corr-1");
    }
}
