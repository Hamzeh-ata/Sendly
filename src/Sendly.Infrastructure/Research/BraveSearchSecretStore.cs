using Sendly.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sendly.Infrastructure.Research;

public class BraveSearchSecretStore : IBraveSearchSecretStore
{
    private readonly string _filePath;
    private readonly ILogger<BraveSearchSecretStore> _logger;
    private readonly object _sync = new();

    public BraveSearchSecretStore(IHostEnvironment environment, ILogger<BraveSearchSecretStore> logger)
    {
        _logger = logger;
        var root = Path.Combine(environment.ContentRootPath, "App_Data", "secrets");
        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "brave-search-api-key");
    }

    public string? ReadApiKey()
    {
        lock (_sync)
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            try
            {
                var value = File.ReadAllText(_filePath).Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read Brave Search API key from secret store");
                return null;
            }
        }
    }

    public void WriteApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Brave Search API key is required.");
        }

        lock (_sync)
        {
            File.WriteAllText(_filePath, apiKey.Trim());
        }
    }

    public void ClearApiKey()
    {
        lock (_sync)
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
    }
}
