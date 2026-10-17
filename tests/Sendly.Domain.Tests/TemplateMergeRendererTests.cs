using Sendly.Domain.Services;

namespace Sendly.Domain.Tests;

public class TemplateMergeRendererTests
{
    [Fact]
    public void Render_ReplacesKnownTags()
    {
        var values = TemplateMergeRenderer.BuildValues(
            "a@example.com",
            "Sara",
            "Acme FM",
            "Facility Management",
            "Riyadh",
            "Saudi Arabia",
            "CEO",
            null);

        var rendered = TemplateMergeRenderer.Render(
            "Hello {{ContactName}} from {{CompanyName}} in {{City}}, {{Country}}",
            values);

        Assert.Equal("Hello Sara from Acme FM in Riyadh, Saudi Arabia", rendered);
    }

    [Fact]
    public void Render_UnknownTagsBecomeEmpty()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CompanyName"] = "Acme"
        };

        var rendered = TemplateMergeRenderer.Render("{{CompanyName}} / {{Missing}}", values);
        Assert.Equal("Acme / ", rendered);
    }
}
