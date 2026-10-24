using Sendly.Application.Abstractions;

namespace Sendly.Application.Tests.Fakes;

public class FakeRobotsTxtService : IRobotsTxtService
{
    private readonly HashSet<string> _disallowed = new(StringComparer.OrdinalIgnoreCase);

    public void Disallow(string urlOrHost)
    {
        _disallowed.Add(urlOrHost.Trim().ToLowerInvariant());
    }

    public Task<bool> IsAllowedAsync(Uri url, string userAgent, CancellationToken cancellationToken)
    {
        var host = url.Host.ToLowerInvariant();
        var full = url.ToString().ToLowerInvariant();
        foreach (var item in _disallowed)
        {
            if (full.Contains(item, StringComparison.Ordinal) || host.Contains(item, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Uri>> GetSitemapUrlsAsync(Uri siteUri, string userAgent, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Uri>>([]);
}
