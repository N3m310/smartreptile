using FluentAssertions;
using SmartReptile.Application.Species;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Species;
using SmartReptile.Tests.Unit.Application;

namespace SmartReptile.Tests.Unit.Species;

/// <summary>
/// The picture half of FR-10's catalogue: a profile may carry one, and what is stored is what the bytes turned out
/// to be. The readings these tests hold to are the ones a client depends on — the URL is present exactly when a
/// picture is, the media type is the sniffed one rather than the declared one, a refused upload writes nothing, and
/// a replacement does not leave the file it replaced behind.
/// </summary>
public class SpeciesProfileImageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A one-pixel PNG — the smallest thing that passes the signature check.</summary>
    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
    ];

    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    private readonly FakeSpeciesProfileStore _store = new();
    private readonly FakeFileStore _files = new();
    private readonly TestClock _clock = new() { UtcNow = Now };
    private readonly SpeciesProfileService _service;
    private readonly Guid _owner = Guid.NewGuid();

    public SpeciesProfileImageTests()
    {
        _store.AddUser(_owner);
        _service = new SpeciesProfileService(_store, _files, _clock);
    }

    // ---- setting ------------------------------------------------------------------------------------

    [Fact]
    public async Task Setting_a_picture_records_the_sniffed_type_and_the_url()
    {
        var profile = Profile();

        var outcome = await Set(profile.Id, PngBytes, "image/png");

        outcome.Succeeded.Should().BeTrue();
        outcome.Profile!.ImageUrl.Should().Be($"/api/v1/species-profiles/{profile.Id}/image");
        outcome.Profile.ImageUpdatedAt.Should().Be(Now);

        var key = profile.ImageStorageKey;
        key.Should().NotBeNull("the row keeps the key of the file it points at");
        key.Should().EndWith(".png", "the extension follows the bytes, not the name the caller used");
        profile.ImageContentType.Should().Be("image/png");
        _files.Read(key!).Should().Equal(PngBytes, "the bytes are stored unchanged");
    }

    [Fact]
    public async Task Setting_a_picture_is_audited_as_a_profile_update_on_the_same_commit()
    {
        var profile = Profile();

        await Set(profile.Id, PngBytes, "image/png");

        _store.SaveCount.Should().Be(1, "the row and its picture commit together");
        _store.AuditEntries.Should().ContainSingle();
        _store.AuditEntries[0].Action.Should().Be(AuditAction.ProfileUpdated);
        _store.AuditEntries[0].AfterJson.Should().Contain("imageUrl");
        _store.AuditEntries[0].BeforeJson.Should().NotContain("imageUrl\":\"/api");
    }

    [Fact]
    public async Task A_declared_type_of_octet_stream_counts_as_no_claim()
    {
        var profile = Profile();

        // What a command-line upload sends when it does not know its own file's type.
        var outcome = await Set(profile.Id, PngBytes, "application/octet-stream");

        outcome.Succeeded.Should().BeTrue();
        profile.ImageContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task A_declared_type_that_is_not_a_picture_is_refused_by_name()
    {
        var profile = Profile();

        var outcome = await Set(profile.Id, PngBytes, "text/html");

        outcome.Problem!.Code.Should().Be("image_type_unsupported");
        outcome.Problem.Errors.Should().ContainSingle().Which.Field.Should().Be("file");
        NothingWasWritten(profile);
    }

    [Fact]
    public async Task Bytes_that_are_not_a_picture_are_refused_whatever_they_claim_to_be()
    {
        var profile = Profile();

        // A file named and labelled a PNG that is not one: the declaration is exactly what this refuses to trust.
        var outcome = await Set(profile.Id, [0x3C, 0x73, 0x63, 0x72, 0x69, 0x70, 0x74, 0x3E], "image/png");

        outcome.Problem!.Code.Should().Be("image_invalid");
        NothingWasWritten(profile);
    }

    [Fact]
    public async Task An_empty_upload_is_refused()
    {
        var profile = Profile();

        var outcome = await Set(profile.Id, [], "image/png");

        outcome.Problem!.Code.Should().Be("image_required");
        NothingWasWritten(profile);
    }

    [Fact]
    public async Task A_picture_over_the_limit_is_refused_by_a_bounded_read()
    {
        var profile = Profile();
        var oversized = new byte[2 * 1024 * 1024 + 1];
        PngBytes.CopyTo(oversized, 0);

        var outcome = await Set(profile.Id, oversized, "image/png");

        outcome.Problem!.Code.Should().Be("image_too_large");
        NothingWasWritten(profile);
    }

    [Fact]
    public async Task Replacing_a_picture_discards_the_file_it_replaced()
    {
        var profile = Profile();

        await Set(profile.Id, PngBytes, "image/png");
        var first = profile.ImageStorageKey!;

        _clock.UtcNow = Now.AddMinutes(1);
        await Set(profile.Id, JpegBytes, "image/jpeg");

        profile.ImageStorageKey.Should().NotBe(first);
        profile.ImageContentType.Should().Be("image/jpeg");
        profile.ImageUpdatedAt.Should().Be(Now.AddMinutes(1), "the URL's cache-busting value moves with the picture");
        _files.Deleted.Should().ContainSingle()
            .Which.Should().Be(first, "the replaced file is deleted once nothing refers to it");
        _files.Written.Should().HaveCount(2);
        _files.Read(first).Should().BeNull();
    }

    [Fact]
    public async Task A_built_in_cannot_be_given_a_picture()
    {
        var builtIn = _store.AddBuiltIn("Arid (desert)", (MetricCode.TempC, 38m, 42m));

        var outcome = await Set(builtIn.Id, PngBytes, "image/png");

        outcome.Problem!.Code.Should().Be("builtin_immutable", "the answer that tells the client to duplicate instead");
        builtIn.ImageStorageKey.Should().BeNull();
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Another_accounts_profile_is_not_found()
    {
        var foreign = _store.AddProfile(Guid.NewGuid(), "Someone else's", (MetricCode.TempC, 26m, 32m));

        var outcome = await Set(foreign.Id, PngBytes, "image/png");

        outcome.Problem!.Code.Should().Be("not_found", "a foreign id must not be distinguishable from a made-up one");
        _files.Written.Should().BeEmpty();
    }

    // ---- removing -----------------------------------------------------------------------------------

    [Fact]
    public async Task Removing_a_picture_clears_the_columns_and_deletes_the_file()
    {
        var profile = Profile();
        await Set(profile.Id, PngBytes, "image/png");
        var key = profile.ImageStorageKey!;

        _clock.UtcNow = Now.AddMinutes(1);
        var outcome = await _service.RemoveImageAsync(profile.Id, _owner, AuditActor.Unknown, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Profile!.ImageUrl.Should().BeNull();
        profile.ImageStorageKey.Should().BeNull();
        profile.ImageContentType.Should().BeNull();
        profile.ImageUpdatedAt.Should().Be(Now.AddMinutes(1), "losing a picture is a change to the profile too");
        _files.Deleted.Should().ContainSingle().Which.Should().Be(key);
        _store.AuditEntries.Should().HaveCount(2, "one row from setting the picture and one from removing it");
    }

    [Fact]
    public async Task Removing_a_picture_that_is_not_there_changes_nothing()
    {
        var profile = Profile();

        var outcome = await _service.RemoveImageAsync(profile.Id, _owner, AuditActor.Unknown, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue("the caller asked for a state and the profile already holds it");
        outcome.Profile!.ImageUrl.Should().BeNull();
        _store.SaveCount.Should().Be(0);
        _store.AuditEntries.Should().BeEmpty("an audit row must not claim a change that did not happen");
        _files.Deleted.Should().BeEmpty();
    }

    // ---- reading ------------------------------------------------------------------------------------

    [Fact]
    public async Task Reading_a_picture_answers_the_stored_bytes_and_type()
    {
        var profile = Profile();
        await Set(profile.Id, JpegBytes, "image/jpeg");

        var outcome = await _service.OpenImageAsync(profile.Id, _owner, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Image!.ContentType.Should().Be("image/jpeg");
        outcome.Image.UpdatedAt.Should().Be(Now);

        using var buffer = new MemoryStream();
        await outcome.Image.Content.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(JpegBytes);
    }

    [Fact]
    public async Task A_profile_without_a_picture_answers_image_not_set()
    {
        var profile = Profile();

        var outcome = await _service.OpenImageAsync(profile.Id, _owner, CancellationToken.None);

        outcome.Problem!.Code.Should().Be("image_not_set");
    }

    [Fact]
    public async Task A_built_in_without_a_picture_is_readable_by_anyone_and_says_image_not_set()
    {
        var builtIn = _store.AddBuiltIn("Arid (desert)", (MetricCode.TempC, 38m, 42m));
        var stranger = Guid.NewGuid();

        var outcome = await _service.OpenImageAsync(builtIn.Id, stranger, CancellationToken.None);

        outcome.Problem!.Code.Should().Be(
            "image_not_set",
            "everyone may see a built-in, so the answer is about the picture rather than about permission");
    }

    [Fact]
    public async Task Another_accounts_picture_is_not_found()
    {
        var owner = Guid.NewGuid();
        _store.AddUser(owner);
        var foreign = _store.AddProfile(owner, "Someone else's", (MetricCode.TempC, 26m, 32m));
        await _service.SetImageAsync(
            foreign.Id,
            owner,
            AuditActor.Unknown,
            new ProfileImageUpload(new MemoryStream(PngBytes), "image/png"),
            CancellationToken.None);

        var outcome = await _service.OpenImageAsync(foreign.Id, _owner, CancellationToken.None);

        outcome.Problem!.Code.Should().Be("not_found");
    }

    [Fact]
    public async Task A_row_whose_file_went_missing_answers_image_not_set()
    {
        var profile = Profile();
        await Set(profile.Id, PngBytes, "image/png");

        // The row still names a file; the file is gone. The client's next step is the same as "no picture", and a
        // catalogue render must not fail over it.
        _files.Lose(profile.ImageStorageKey!);

        var outcome = await _service.OpenImageAsync(profile.Id, _owner, CancellationToken.None);

        outcome.Problem!.Code.Should().Be("image_not_set");
        profile.ImageStorageKey.Should().NotBeNull("the inconsistency stays visible in the data");
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private SpeciesProfile Profile() => _store.AddProfile(_owner, "My species", (MetricCode.TempC, 26m, 32m));

    private Task<SpeciesProfileOutcome> Set(Guid profileId, byte[] bytes, string declaredType) =>
        _service.SetImageAsync(
            profileId,
            _owner,
            AuditActor.Unknown,
            new ProfileImageUpload(new MemoryStream(bytes), declaredType),
            CancellationToken.None);

    private void NothingWasWritten(SpeciesProfile profile)
    {
        profile.ImageStorageKey.Should().BeNull();
        _files.Written.Should().BeEmpty("a refused upload must not become a file either");
        _store.SaveCount.Should().Be(0);
        _store.AuditEntries.Should().BeEmpty();
    }
}
