using FluentAssertions;
using SmartReptile.Domain.Auditing;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// The closed entity vocabulary of an audit row, and the resolution the <c>GET /audit</c> filter uses (roadmap 3.7).
/// </summary>
public class AuditEntityTests
{
    [Theory]
    [InlineData("Device", "Device")]
    [InlineData("device", "Device")]
    [InlineData("  Device  ", "Device")]
    [InlineData("ALERT", "Alert")]
    [InlineData("metricsilence", "MetricSilence")]
    [InlineData("MetricSilence", "MetricSilence")]
    [InlineData("speciesprofile", "SpeciesProfile")]
    public void TryParse_resolves_a_known_name_to_its_stored_spelling(string value, string expected)
    {
        AuditEntity.TryParse(value, out var entity).Should().BeTrue();
        entity.Should().Be(expected);
    }

    [Theory]
    [InlineData("Terrarium")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_refuses_what_this_build_never_writes(string? value)
    {
        AuditEntity.TryParse(value, out var entity).Should().BeFalse();
        entity.Should().BeEmpty();
    }

    [Fact]
    public void Every_factory_names_an_entity_this_vocabulary_knows()
    {
        // The four writers and the filter have to agree; a literal drifting out of the list would make a row
        // unfilterable (and, worse, silently invisible to `?entity=`).
        var written = new[]
        {
            AuditLog.ForDevice(AuditAction.DeviceClaimed, Guid.NewGuid(), "sr-abc123", Guid.NewGuid(), AuditActor.Unknown, DateTimeOffset.UnixEpoch).EntityName,
            AuditLog.ForAlert(AuditAction.AlertAcknowledged, 7, Guid.NewGuid(), Guid.NewGuid(), AuditActor.Unknown, DateTimeOffset.UnixEpoch).EntityName,
            AuditLog.ForSilence(AuditAction.MetricSilenced, Guid.NewGuid(), Guid.NewGuid(), AuditActor.Unknown, DateTimeOffset.UnixEpoch).EntityName,
            AuditLog.ForSpeciesProfile(AuditAction.ProfileCreated, Guid.NewGuid(), Guid.NewGuid(), AuditActor.Unknown, DateTimeOffset.UnixEpoch).EntityName,
        };

        written.Should().OnlyContain(entity => AuditEntity.All.Contains(entity));
        AuditEntity.All.Should().BeEquivalentTo(written);
    }
}
