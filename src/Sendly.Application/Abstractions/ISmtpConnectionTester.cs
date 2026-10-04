using Sendly.Application.DTOs;
using Sendly.Domain.Enums;

namespace Sendly.Application.Abstractions;

public interface ISmtpConnectionTester
{
    Task<SmtpTestResultDto> TestAsync(SmtpConnectionTestRequest request, CancellationToken cancellationToken);
}

public sealed class SmtpConnectionTestRequest
{
    public required string Host { get; init; }

    public required int Port { get; init; }

    public required SmtpSecurityMode SecurityMode { get; init; }

    public required string Username { get; init; }

    public required string Password { get; init; }
}
