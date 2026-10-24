using System.Net.Http;
using Sendly.Application.Abstractions;

namespace Sendly.Application.Tests.Fakes;

public sealed class LiveHttpFetcher : ISafeHttpFetcher, IDisposable
{
    private readonly HttpClient _http;

    public List<Uri> RequestedUrls { get; } = [];

    public LiveHttpFetcher()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SendlyResearchDiagnostic/1.0");
    }

    public async Task<SafeHttpFetchResult> FetchAsync(
        SafeHttpFetchRequest request,
        CancellationToken cancellationToken)
    {
        RequestedUrls.Add(request.Url);
        try
        {
            using var response = await _http.GetAsync(request.Url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "text/html";
            return new SafeHttpFetchResult
            {
                RequestedUrl = request.Url,
                FinalUrl = response.RequestMessage?.RequestUri ?? request.Url,
                Succeeded = response.IsSuccessStatusCode,
                StatusCode = (int)response.StatusCode,
                ContentType = contentType,
                Body = body,
                ContentLengthBytes = body.Length,
                Error = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            return new SafeHttpFetchResult
            {
                RequestedUrl = request.Url,
                FinalUrl = request.Url,
                Succeeded = false,
                StatusCode = 0,
                Error = ex.Message
            };
        }
    }

    public void Dispose() => _http.Dispose();
}
