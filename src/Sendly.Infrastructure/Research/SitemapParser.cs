using System.Xml.Linq;
using Sendly.Application.Abstractions;

namespace Sendly.Infrastructure.Research;

public class SitemapParser : ISitemapParser
{
    public IReadOnlyList<Uri> Parse(string xmlContent, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return [];
        }

        try
        {
            var document = XDocument.Parse(xmlContent, LoadOptions.None);
            var result = new List<Uri>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var loc in document.Descendants().Where(x => x.Name.LocalName == "loc"))
            {
                var value = loc.Value?.Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!Uri.TryCreate(baseUri, value, out var uri))
                {
                    continue;
                }

                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                {
                    continue;
                }

                var key = uri.ToString();
                if (seen.Add(key))
                {
                    result.Add(uri);
                }
            }

            return result;
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }
    }
}
