using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Enums;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace Sendly.Infrastructure.Email;

public class MailKitSmtpConnectionTester : ISmtpConnectionTester
{
    public async Task<EmailTestResultDto> TestAsync(SmtpConnectionTestRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new SmtpClient();
            client.Timeout = 30000;

            var socketOptions = MapSocketOptions(request.SecurityMode);
            await client.ConnectAsync(request.Host, request.Port, socketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(request.Username))
            {
                await client.AuthenticateAsync(request.Username, request.Password, cancellationToken);
            }

            await client.DisconnectAsync(true, cancellationToken);

            return new EmailTestResultDto
            {
                Success = true,
                Message = "SMTP connection and authentication succeeded."
            };
        }
        catch (Exception ex)
        {
            return new EmailTestResultDto
            {
                Success = false,
                Message = Sanitize(ex.Message)
            };
        }
    }

    private static SecureSocketOptions MapSocketOptions(SmtpSecurityMode mode)
    {
        return mode switch
        {
            SmtpSecurityMode.None => SecureSocketOptions.None,
            SmtpSecurityMode.StartTls => SecureSocketOptions.StartTls,
            SmtpSecurityMode.SslTls => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.StartTls
        };
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "SMTP connection failed.";
        }

        var sanitized = message.Replace("password", "****", StringComparison.OrdinalIgnoreCase);
        return sanitized.Length > 500 ? sanitized[..500] : sanitized;
    }
}
