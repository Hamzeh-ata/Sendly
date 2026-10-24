using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Application.Services.MapResearch;

namespace Sendly.Application.Tests;

public class OsmPlaceContactMapperTests
{
    [Fact]
    public void Maps_ContactPhone_And_ContactEmail_Tags()
    {
        var place = new MapPlaceRecord
        {
            SourceObjectKey = "osm:node/1",
            SourceUrl = "https://www.openstreetmap.org/node/1"
        };
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "Example School",
            ["contact:phone"] = "+971 4 123 4567; +971 50 111 2222",
            ["contact:email"] = "info@example-school.test",
            ["contact:website"] = "https://example-school.test",
            ["amenity"] = "school",
            ["addr:street"] = "Main Street",
            ["addr:housenumber"] = "10",
            ["addr:city"] = "Dubai",
            ["addr:postcode"] = "00000",
            ["addr:country"] = "AE",
            ["opening_hours"] = "Mo-Fr 08:00-15:00",
            ["fax"] = "+971 4 999 0000",
            ["operator"] = "Example Group"
        };

        OsmPlaceContactMapper.ApplyTags(place, tags, DateTimeOffset.UtcNow);

        Assert.Equal("Example School", place.Name);
        Assert.Equal("+971 4 123 4567", place.Phone);
        Assert.Equal(2, place.Phones.Count);
        Assert.Equal("info@example-school.test", place.Email);
        Assert.Equal("https://example-school.test", place.Website);
        Assert.Equal("+971 4 999 0000", place.Fax);
        Assert.Equal("10 Main Street", place.StreetAddress);
        Assert.Equal("00000", place.Postcode);
        Assert.Contains("amenity=school", place.CategoryTags);
        Assert.False(string.IsNullOrWhiteSpace(place.RawTagsJson));
        Assert.True(place.FieldProvenance.ContainsKey("phone"));
        Assert.Equal("contact:phone", place.FieldProvenance["phone"].SourceTagKey);
        Assert.True(place.FieldProvenance.ContainsKey("email"));
        Assert.Equal("contact:email", place.FieldProvenance["email"].SourceTagKey);
    }

    [Fact]
    public void Does_Not_Overwrite_Existing_Valid_Fields_With_Empty()
    {
        var place = new MapPlaceRecord
        {
            Phone = "+971500000000",
            Email = "keep@example.test",
            Website = "https://keep.example.test",
            SourceObjectKey = "osm:way/2"
        };
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "Named Place",
            ["amenity"] = "school"
        };

        OsmPlaceContactMapper.ApplyTags(place, tags, DateTimeOffset.UtcNow);

        Assert.Equal("+971500000000", place.Phone);
        Assert.Equal("keep@example.test", place.Email);
        Assert.Equal("https://keep.example.test", place.Website);
    }

    [Fact]
    public void Html_Mailto_And_Tel_Extraction_Works()
    {
        var html = """
            <html><body>
            <a href="mailto:hello@school.test">Email</a>
            <a href="tel:+97141234567">Call</a>
            <script type="application/ld+json">
            {"@type":"Organization","email":"office@school.test","telephone":"+97145555555"}
            </script>
            </body></html>
            """;
        var extracted = HtmlPageExtractor.Extract(html, new Uri("https://school.test/contact"));
        Assert.Contains("hello@school.test", extracted.Emails);
        Assert.Contains(extracted.Phones, x => x.Contains("97141234567", StringComparison.Ordinal));
    }
}
