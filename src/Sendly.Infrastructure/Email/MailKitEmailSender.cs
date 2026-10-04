using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Sendly.Infrastructure.Email;

public class MailKitEmailSender : IEmailSender
{
    public async Task<EmailSendResult> SendAsync(EmailSendRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var message = BuildMessage(request);
            using var client = new SmtpClient();
            client.Timeout = 30000;

            var socketOptions = MapSocketOptions(request.SecurityMode);
            await client.ConnectAsync(request.Host, request.Port, socketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(request.Username))
            {
                await client.AuthenticateAsync(request.Username, request.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            return new EmailSendResult
            {
                Success = true,
                Message = "Accepted by SMTP server."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new EmailSendResult
            {
                Success = false,
                IsTransientFailure = IsTransient(ex),
                Message = Sanitize(ex.Message)
            };
        }
    }

    private static MimeMessage BuildMessage(EmailSendRequest request)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(request.SenderDisplayName, request.SenderEmail));
        message.To.Add(MailboxAddress.Parse(request.ToEmail));
        message.Subject = request.Subject;

        if (!string.IsNullOrWhiteSpace(request.ReplyToEmail))
        {
            message.ReplyTo.Add(MailboxAddress.Parse(request.ReplyToEmail));
        }

        var builder = new BodyBuilder
        {
            HtmlBody = request.HtmlBody,
            TextBody = request.PlainTextBody
        };

        foreach (var attachment in request.Attachments)
        {
            builder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        message.Body = builder.ToMessageBody();
        return message;
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

    private static bool IsTransient(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();
        if (message.Contains("timeout") ||
            message.Contains("temporarily") ||
            message.Contains("try again") ||
            message.Contains("4."))
        {
            return true;
        }

        return ex is TimeoutException or IOException;
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "SMTP send failed.";
        }

        var sanitized = message.Replace("password", "****", StringComparison.OrdinalIgnoreCase);
        return sanitized.Length > 500 ? sanitized[..500] : sanitized;
    }
}
