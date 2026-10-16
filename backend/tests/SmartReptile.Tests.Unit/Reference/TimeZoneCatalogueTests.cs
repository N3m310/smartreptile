using FluentAssertions;
using SmartReptile.Application.Reference;
using SmartReptile.Infrastructure.Persistence;
using SmartReptile.Tests.Unit.Terrariums;

namespace SmartReptile.Tests.Unit.Reference;

/// <summary>
/// The time-zone catalogue and the reference route that serves it (TASKS 2026-10-14, item 11). Asserted without a
/// database: the catalogue is a literal list, so its shape — unique ids, sane offsets, west-to-east order — is
/// checkable where it is written rather than only where it is seeded.
/// </summary>
public class TimeZoneCatalogueTests
{
    private static readonly IReadOnlyList<ReferenceDataSeeder.TimeZoneSeed> Catalogue = ReferenceDataSeeder.Catalogue;

    [Fact]
    public void Every_zone_id_appears_once()
    {
        Catalogue.Select(seed => seed.IanaId)
            .Should().OnlyHaveUniqueItems("the id is the primary key, so a repeat would fail the seed on start-up");
    }

    [Fact]
    public void Every_zone_is_one_the_platform_can_resolve()
    {
        // The catalogue is what a terrarium may name, and the phase resolver converts an instant into that zone at
        // read time (ADR-015). An id no time-zone database knows would degrade to UTC silently — a terrarium whose
        // day/night split is wrong without anything saying so.
        foreach (var seed in Catalogue)
        {
            TimeZoneInfo.TryFindSystemTimeZoneById(seed.IanaId, out _)
                .Should().BeTrue($"'{seed.IanaId}' is offered by the picker, so the server must be able to use it");
        }
    }

    [Fact]
    public void Every_offset_is_a_quarter_hour_inside_the_range_the_world_uses()
    {
        Catalogue.Should().OnlyContain(seed => seed.StandardUtcOffsetMinutes % 15 == 0);
        Catalogue.Should().OnlyContain(seed => seed.StandardUtcOffsetMinutes >= -720);
        Catalogue.Should().OnlyContain(seed => seed.StandardUtcOffsetMinutes <= 840);
    }

    [Fact]
    public void The_list_is_ordered_west_to_east_and_the_stored_position_follows_it()
    {
        // SortOrder is the index in this list, which is what the picker orders by; a list that drifted out of order
        // would be corrected on the next start-up, so it is worth catching here where the reason is visible.
        Catalogue.Select(seed => seed.StandardUtcOffsetMinutes).Should().BeInAscendingOrder();

        var zones = new FakeTimeZoneStore().Zones;

        zones.Select(zone => zone.SortOrder).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        zones.Select(zone => zone.IanaId).Should().Equal(Catalogue.Select(seed => seed.IanaId));
    }

    [Fact]
    public void Every_row_carries_a_label_and_a_country_except_the_one_that_has_neither()
    {
        Catalogue.Should().OnlyContain(seed => seed.DisplayName.Length > 0);

        // The column is two characters wide, so only a zone belonging to no country is empty.
        Catalogue.Where(seed => seed.CountryCode.Length == 0)
            .Select(seed => seed.IanaId)
            .Should().ContainSingle().Which.Should().Be("UTC");

        Catalogue.Where(seed => seed.CountryCode.Length > 0).Should().OnlyContain(seed => seed.CountryCode.Length == 2);
    }

    [Fact]
    public void Vietnam_is_in_the_catalogue_with_its_own_offset_and_unchanged_by_daylight_saving()
    {
        var vietnam = Catalogue.Single(seed => seed.IanaId == "Asia/Ho_Chi_Minh");

        vietnam.CountryCode.Should().Be("VN");
        vietnam.StandardUtcOffsetMinutes.Should().Be(420);

        // The single-offset fact is what the seeded catalogue replaced a province table with (TASKS 2026-10-14,
        // item 11): the country has one zone, so a list of provinces could only have repeated this row.
        Catalogue.Count(seed => seed.CountryCode == "VN").Should().Be(1);
    }

    [Theory]
    [InlineData(0, "UTC")]
    [InlineData(420, "UTC+07:00")]
    [InlineData(-180, "UTC-03:00")]
    [InlineData(-210, "UTC-03:30")]
    [InlineData(345, "UTC+05:45")]
    [InlineData(840, "UTC+14:00")]
    public void Offsets_are_rendered_the_way_a_keeper_reads_them(int minutes, string expected) =>
        ReferenceService.FormatOffset(minutes).Should().Be(expected);

    [Fact]
    public async Task The_route_projection_keeps_the_order_and_repeats_the_offset_twice()
    {
        var service = new ReferenceService(new FakeTimeZoneStore());

        var zones = await service.ListTimeZonesAsync(CancellationToken.None);

        zones.Should().HaveCount(ReferenceDataSeeder.TimeZoneCount);
        zones.Select(zone => zone.IanaId).Should().Equal(Catalogue.Select(seed => seed.IanaId));

        // Both spellings of the same number: a client that groups by offset needs the minutes, a client that shows a
        // label needs the formatted string, and neither should have to invent one from the other.
        var hanoi = zones.Single(zone => zone.IanaId == "Asia/Ho_Chi_Minh");
        hanoi.UtcOffsetMinutes.Should().Be(420);
        hanoi.UtcOffset.Should().Be("UTC+07:00");
        hanoi.DisplayName.Should().Be("Hanoi, Ho Chi Minh City");
    }
}
