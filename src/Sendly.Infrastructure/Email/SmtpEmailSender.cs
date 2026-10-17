using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Sendly.Infrastructure.Email;

public class SmtpEmailSender : IProviderEmailSender
{
    public EmailProvider Provider => EmailProvider.Smtp;

    public async Task<EmailSendResult> SendAsync(
        EmailAccountCredentials credentials,
        EmailDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var message = BuildMessage(credentials, request);
            using var client = new SmtpClient();
            client.Timeout = 30000;

            var socketOptions = MapSocketOptions(credentials.SecurityMode ?? SmtpSecurityMode.StartTls);
            await client.ConnectAsync(credentials.Host!, credentials.Port!.Value, socketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(credentials.Username))
            {
                await client.AuthenticateAsync(credentials.Username, credentials.Password, cancellationToken);
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

    private static MimeMessage BuildMessage(EmailAccountCredentials credentials, EmailDeliveryRequest request)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            credentials.SenderDisplayName ?? credentials.DisplayName,
            credentials.SenderEmail ?? credentials.EmailAddress));
        message.To.Add(MailboxAddress.Parse(request.ToEmail));
        message.Subject = request.Subject;

        if (!string.IsNullOrWhiteSpace(credentials.ReplyToEmail))
        {
            message.ReplyTo.Add(MailboxAddress.Parse(credentials.ReplyToEmail));
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
            SmtpSecurityMode.SslTls => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.StartTls
        };
    }

    private static bool IsTransient(Exception ex)
    {
        var text = ex.Message.ToLowerInvariant();
        return text.Contains("timeout")
            || text.Contains("temporar")
            || text.Contains("try again")
            || text.Contains("4.7.")
            || text.Contains("connection");
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "SMTP send failed.";
        }

        return message.Length > 500 ? message[..500] : message;
    }
}
