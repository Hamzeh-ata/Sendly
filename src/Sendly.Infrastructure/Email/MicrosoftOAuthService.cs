using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Email;

public class MicrosoftOAuthService : IMicrosoftOAuthService, IMicrosoftTokenService
{
    private const string Scopes = "openid profile email offline_access Mail.Send User.Read";

    private static readonly ConcurrentDictionary<string, DateTimeOffset> PendingStates = new();

    private readonly ISendlyDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly MicrosoftGraphOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    public MicrosoftOAuthService(
        ISendlyDbContext db,
        ISecretProtector secretProtector,
        IOptions<MicrosoftGraphOptions> options,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _secretProtector = secretProtector;
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ClientId)
        && !string.IsNullOrWhiteSpace(_options.ClientSecret)
        && !string.IsNullOrWhiteSpace(_options.RedirectUri);

    public string BuildAuthorizeUrl(string state)
    {
        EnsureConfigured();
        PendingStates[state] = DateTimeOffset.UtcNow.AddMinutes(15);
        CleanupExpiredStates();

        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = _options.RedirectUri,
            ["response_mode"] = "query",
            ["scope"] = Scopes,
            ["state"] = state,
            ["prompt"] = "select_account"
        };

        return $"https://login.microsoftonline.com/{Uri.EscapeDataString(_options.TenantId)}/oauth2/v2.0/authorize?{BuildQuery(query)}";
    }

    public bool ValidateState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        if (!PendingStates.TryRemove(state, out var expiresAt))
        {
            return false;
        }

        return expiresAt >= DateTimeOffset.UtcNow;
    }

    public async Task<MicrosoftOAuthCompletionResult> CompleteAsync(string code, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        try
        {
            var token = await ExchangeCodeAsync(code, cancellationToken);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                return new MicrosoftOAuthCompletionResult
                {
                    Success = false,
                    Message = "Microsoft sign-in failed. Try connecting again."
                };
            }

            var profile = await LoadProfileAsync(token.AccessToken, cancellationToken);
            var account = await UpsertAccountAsync(token, profile, cancellationToken);

            return new MicrosoftOAuthCompletionResult
            {
                Success = true,
                AccountId = account.Id,
                Message = "Microsoft account connected."
            };
        }
        catch (Exception)
        {
            return new MicrosoftOAuthCompletionResult
            {
                Success = false,
                Message = "Could not complete Microsoft sign-in."
            };
        }
    }

    public async Task<string?> GetValidAccessTokenAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _db.EmailAccounts.FirstOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        if (account is null || account.Provider != EmailProvider.MicrosoftGraph || !account.IsActive)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(account.AccessTokenProtected)
            && account.TokenExpiresAtUtc.HasValue
            && account.TokenExpiresAtUtc.Value > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return _secretProtector.Unprotect(account.AccessTokenProtected);
        }

        if (string.IsNullOrWhiteSpace(account.RefreshTokenProtected))
        {
            return null;
        }

        var refreshToken = _secretProtector.Unprotect(account.RefreshTokenProtected);
        var token = await RefreshAsync(refreshToken, cancellationToken);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            return null;
        }

        ApplyTokenResult(account, token);
        account.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return token.AccessToken;
    }

    public async Task DisconnectAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _db.EmailAccounts.FirstOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        if (account is null)
        {
            return;
        }

        account.AccessTokenProtected = null;
        account.RefreshTokenProtected = null;
        account.TokenExpiresAtUtc = null;
        account.IsActive = false;
        account.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<TokenResponse?> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri,
            ["scope"] = Scopes
        };

        return await RequestTokenAsync(form, cancellationToken);
    }

    private async Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = Scopes
        };

        return await RequestTokenAsync(form, cancellationToken);
    }

    private async Task<TokenResponse?> RequestTokenAsync(
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("MicrosoftGraph");
        var tokenUrl = $"https://login.microsoftonline.com/{Uri.EscapeDataString(_options.TenantId)}/oauth2/v2.0/token";
        using var content = new FormUrlEncodedContent(form);
        using var response = await client.PostAsync(tokenUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        return new TokenResponse
        {
            AccessToken = root.GetProperty("access_token").GetString() ?? string.Empty,
            RefreshToken = root.TryGetProperty("refresh_token", out var refresh)
                ? refresh.GetString()
                : null,
            ExpiresIn = root.TryGetProperty("expires_in", out var expires)
                ? expires.GetInt32()
                : 3600
        };
    }

    private async Task<EmailAccount> UpsertAccountAsync(
        TokenResponse token,
        MicrosoftProfile profile,
        CancellationToken cancellationToken)
    {
        var email = profile.Email;
        var all = await _db.EmailAccounts
            .Where(x => x.Provider == EmailProvider.MicrosoftGraph)
            .ToListAsync(cancellationToken);
        EmailAccount? existing = null;
        foreach (var item in all)
        {
            if (string.Equals(item.EmailAddress, email, StringComparison.OrdinalIgnoreCase))
            {
                existing = item;
                break;
            }
        }

        if (existing is null)
        {
            existing = new EmailAccount
            {
                Provider = EmailProvider.MicrosoftGraph,
                EmailAddress = email,
                DisplayName = profile.DisplayName,
                IsActive = true,
                IsDefault = await _db.EmailAccounts.CountAsync(cancellationToken) == 0,
                MicrosoftUserId = profile.Id,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            if (existing.IsDefault)
            {
                await ClearDefaultFlagsAsync(cancellationToken);
            }

            _db.EmailAccounts.Add(existing);
        }
        else
        {
            existing.DisplayName = profile.DisplayName;
            existing.IsActive = true;
            existing.MicrosoftUserId = profile.Id;
        }

        ApplyTokenResult(existing, token);
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    private void ApplyTokenResult(EmailAccount account, TokenResponse token)
    {
        account.AccessTokenProtected = _secretProtector.Protect(token.AccessToken);
        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            account.RefreshTokenProtected = _secretProtector.Protect(token.RefreshToken);
        }

        account.TokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 60));
    }

    private async Task<MicrosoftProfile> LoadProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("MicrosoftGraph");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://graph.microsoft.com/v1.0/me?$select=id,displayName,mail,userPrincipalName");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        var displayName = root.TryGetProperty("displayName", out var nameProp) ? nameProp.GetString() : null;
        var mail = root.TryGetProperty("mail", out var mailProp) ? mailProp.GetString() : null;
        var upn = root.TryGetProperty("userPrincipalName", out var upnProp) ? upnProp.GetString() : null;
        var email = !string.IsNullOrWhiteSpace(mail) ? mail! : upn ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Microsoft account email was not available.");
        }

        return new MicrosoftProfile
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? email : displayName!,
            Email = email.Trim()
        };
    }

    private async Task ClearDefaultFlagsAsync(CancellationToken cancellationToken)
    {
        var defaults = await _db.EmailAccounts.Where(x => x.IsDefault).ToListAsync(cancellationToken);
        foreach (var item in defaults)
        {
            item.IsDefault = false;
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Microsoft Graph OAuth is not configured on the server.");
        }
    }

    private static void CleanupExpiredStates()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in PendingStates)
        {
            if (pair.Value < now)
            {
                PendingStates.TryRemove(pair.Key, out _);
            }
        }
    }

    private static string BuildQuery(Dictionary<string, string> values)
    {
        var parts = new List<string>();
        foreach (var pair in values)
        {
            parts.Add($"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
        }

        return string.Join('&', parts);
    }

    private sealed class TokenResponse
    {
        public required string AccessToken { get; init; }

        public string? RefreshToken { get; init; }

        public int ExpiresIn { get; init; }
    }

    private sealed class MicrosoftProfile
    {
        public string? Id { get; init; }

        public required string DisplayName { get; init; }

        public required string Email { get; init; }
    }
}
