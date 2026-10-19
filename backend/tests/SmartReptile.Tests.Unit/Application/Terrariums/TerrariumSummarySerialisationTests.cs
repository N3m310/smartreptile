using System.Text.Json;
using FluentAssertions;
using SmartReptile.Application.Terrariums;

namespace SmartReptile.Tests.Unit.Application.Terrariums;

/// <summary>
/// The item shape `07-appendices/03` §4.2 documents is a contract with the app, the dashboard and Swagger, and
/// <c>rowVersion</c> is the one field that is present on some answers and absent on others — so which answers carry
/// it is asserted here rather than only observed on the wire. The single-terrarium answers repeat the <c>ETag</c>
/// there so an update can be authored by a client that cannot read response headers (TASKS 2026-10-14, item 4).
/// </summary>
public class TerrariumSummarySerialisationTests
{
    /// <summary>The API's own options: minimal APIs answer with the web defaults (camelCase, nulls written).</summary>
    private static readonly JsonSerializerOptions Options = JsonSerializerOptions.Web;

    private static TerrariumSummary Summary() => new(
        Guid.NewGuid(),
        "Bể 1",
        Guid.NewGuid(),
        "Arid (desert)",
        "Shelf A",
        null,
        "Asia/Ho_Chi_Minh",
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        null,
        null,
        0);

    [Fact]
    public void A_summary_that_has_a_concurrency_token_serialises_it_as_rowVersion()
    {
        var json = JsonSerializer.Serialize(Summary() with { RowVersion = "AAAAAAAAB9U=" }, Options);

        json.Should().Contain(
            "\"rowVersion\":\"AAAAAAAAB9U=\"",
            "this is the same base64 the ETag header carries, so either may be sent back as If-Match");
    }

    [Fact]
    public void A_summary_without_one_omits_the_field_entirely()
    {
        var json = JsonSerializer.Serialize(Summary(), Options);

        json.Should().NotContain(
            "rowVersion",
            "a list item is read-only: a null field would read as a token the client may send back");
    }
}
