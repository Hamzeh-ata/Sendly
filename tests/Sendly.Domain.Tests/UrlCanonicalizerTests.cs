using Sendly.Domain.Services;

namespace Sendly.Domain.Tests;

public class UrlCanonicalizerTests
{
    [Fact]
    public void Canonicalize_strips_tracking_parameters_and_fragment()
    {
        var result = UrlCanonicalizer.Canonicalize(
            "https://WWW.Example.com/path/?utm_source=x&id=1#section");

        Assert.Equal("https://www.example.com/path?id=1", result);
    }

    [Fact]
    public void Canonicalize_rejects_non_http_schemes()
    {
        Assert.Null(UrlCanonicalizer.Canonicalize("ftp://example.com/file"));
    }

    [Fact]
    public void ExtractDomain_normalizes_www()
    {
        Assert.Equal("example.com", UrlCanonicalizer.ExtractDomain("https://www.example.com/about"));
    }
}
