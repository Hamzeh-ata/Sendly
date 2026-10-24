using Sendly.Infrastructure.Research;

namespace Sendly.Application.Tests;

public class SitemapParserTests
{
    [Fact]
    public void Parse_reads_loc_entries()
    {
        var parser = new SitemapParser();
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url><loc>https://example.com/about</loc></url>
              <url><loc>https://example.com/contact</loc></url>
            </urlset>
            """;

        var urls = parser.Parse(xml, new Uri("https://example.com"));

        Assert.Equal(2, urls.Count);
        Assert.Contains(urls, x => x.AbsoluteUri.Contains("/about", StringComparison.Ordinal));
        Assert.Contains(urls, x => x.AbsoluteUri.Contains("/contact", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_returns_empty_for_invalid_xml()
    {
        var parser = new SitemapParser();
        var urls = parser.Parse("<not-valid", new Uri("https://example.com"));
        Assert.Empty(urls);
    }
}
