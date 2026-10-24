using Sendly.Application.Services;

namespace Sendly.Application.Tests;

public class HtmlPageExtractorTests
{
    [Fact]
    public void Extract_reads_mailto_email_and_title()
    {
        var html = """
            <html>
              <head>
                <title>Acme Cleaning | Home</title>
                <link rel="canonical" href="https://acme.example/home" />
              </head>
              <body>
                <a href="mailto:sales@acme.example">Email us</a>
                <a href="/contact">Contact</a>
              </body>
            </html>
            """;

        var data = HtmlPageExtractor.Extract(html, new Uri("https://acme.example/"));

        Assert.Equal("Acme Cleaning | Home", data.Title);
        Assert.Contains("sales@acme.example", data.Emails);
        Assert.Contains(data.SameOriginLinks, x => x.Contains("/contact", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrWhiteSpace(data.ContentHash));
    }
}
