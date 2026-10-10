using FluentAssertions;
using SmartReptile.Application.Ingest;

namespace SmartReptile.Tests.Unit.Application.Ingest;

/// <summary>
/// The wire form of a device credential (rule V-05, §07-appendices/03 §1). Worth pinning down because every
/// failure here is silent: a value that parses to an empty secret would reach the hash comparison and be refused
/// exactly like a wrong one, so a parsing bug would look like an authentication bug forever.
/// </summary>
public class DeviceCredentialHeaderTests
{
    [Theory]
    [InlineData("Device sr-3f9a2c.MFRGGZDFMZTWQ2LK", "sr-3f9a2c", "MFRGGZDFMZTWQ2LK")]
    [InlineData("device sr-3f9a2c.MFRGGZDFMZTWQ2LK", "sr-3f9a2c", "MFRGGZDFMZTWQ2LK")]
    [InlineData("DEVICE sr-3f9a2c.MFRGGZDFMZTWQ2LK", "sr-3f9a2c", "MFRGGZDFMZTWQ2LK")]
    [InlineData("Device   sr-3f9a2c.MFRGGZDFMZTWQ2LK  ", "sr-3f9a2c", "MFRGGZDFMZTWQ2LK")]
    public void TryParse_accepts_the_documented_scheme_regardless_of_case_or_padding(
        string header,
        string expectedId,
        string expectedSecret)
    {
        DeviceCredentialHeader.TryParse(header, out var devicePublicId, out var presentedSecret)
            .Should().BeTrue();

        devicePublicId.Should().Be(expectedId);
        presentedSecret.Should().Be(expectedSecret);
    }

    [Fact]
    public void TryParse_splits_on_the_first_dot_so_an_id_cannot_swallow_the_secret()
    {
        // A dotted id is not legal, but the failure mode matters: splitting on the last dot would read this as
        // device `sr-x.y` with the secret `z`, i.e. a *different* device's credential presented successfully.
        DeviceCredentialHeader.TryParse("Device sr-x.y.z", out var devicePublicId, out var presentedSecret)
            .Should().BeTrue();

        devicePublicId.Should().Be("sr-x");
        presentedSecret.Should().Be("y.z");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bearer a-keeper-jwt")]
    [InlineData("DeviceCode sr-3f9a2c.MFRGGZDF")]
    [InlineData("Device")]
    [InlineData("Device ")]
    [InlineData("Device sr-3f9a2c")]
    [InlineData("Device sr-3f9a2c.")]
    [InlineData("Device .MFRGGZDFMZTWQ2LK")]
    [InlineData("Device .")]
    public void TryParse_refuses_anything_without_two_non_empty_parts(string? header)
    {
        DeviceCredentialHeader.TryParse(header, out var devicePublicId, out var presentedSecret)
            .Should()
            .BeFalse();

        // Both outs stay empty rather than holding a partial parse, so a caller that ignores the false cannot send
        // a half-credential into the pipeline by accident.
        devicePublicId.Should().BeEmpty();
        presentedSecret.Should().BeEmpty();
    }

    [Fact]
    public void TryParse_is_not_fooled_by_a_scheme_that_merely_starts_with_the_same_letters()
    {
        // `Devices` is not `Device`: accepting it would let a caller choose which parser reads their credential.
        DeviceCredentialHeader.TryParse("Devices sr-3f9a2c.MFRGGZDF", out _, out _).Should().BeFalse();
    }
}
