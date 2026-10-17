namespace Sendly.Application.Abstractions;

public interface IMicrosoftTokenService
{
    Task<string?> GetValidAccessTokenAsync(Guid accountId, CancellationToken cancellationToken);

    Task DisconnectAsync(Guid accountId, CancellationToken cancellationToken);
}

public interface IMicrosoftOAuthService
{
    bool IsConfigured { get; }

    string BuildAuthorizeUrl(string state);

    bool ValidateState(string? state);

    Task<MicrosoftOAuthCompletionResult> CompleteAsync(string code, CancellationToken cancellationToken);
}

public sealed class MicrosoftOAuthCompletionResult
{
    public required bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public Guid? AccountId { get; init; }
}
