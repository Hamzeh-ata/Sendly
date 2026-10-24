namespace Sendly.Application.Abstractions;

public interface ISitemapParser
{
    IReadOnlyList<Uri> Parse(string xmlContent, Uri baseUri);
}
