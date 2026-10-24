namespace Sendly.Application.Abstractions;

public interface IRobotsTxtService
{
    Task<bool> IsAllowedAsync(Uri url, string userAgent, CancellationToken cancellationToken);

    Task<IReadOnlyList<Uri>> GetSitemapUrlsAsync(Uri siteUrl, string userAgent, CancellationToken cancellationToken);
}
