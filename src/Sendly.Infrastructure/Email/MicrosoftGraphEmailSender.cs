using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using System.Text;
using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;

namespace Sendly.Infrastructure.Email;

public class MicrosoftGraphEmailSender : IProviderEmailSender
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public MicrosoftGraphEmailSender(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public EmailProvider Provider => EmailProvider.MicrosoftGraph;

    public async Task<EmailSendResult> SendAsync(
        EmailAccountCredentials credentials,
        EmailDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentials.AccessToken))
        {
            return new EmailSendResult
            {
                Success = false,
                Message = "Microsoft account authentication expired. Reconnect the account."
            };
        }

        try
        {
            var payload = BuildSendMailPayload(credentials, request);
            var client = _httpClientFactory.CreateClient("MicrosoftGraph");
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/sendMail");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new EmailSendResult
                {
                    Success = true,
                    Message = "Accepted by Microsoft Graph."
                };
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new EmailSendResult
            {
                Success = false,
                IsTransientFailure = IsTransientStatus((int)response.StatusCode),
                Message = MapGraphError((int)response.StatusCode, body)
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
                IsTransientFailure = true,
                Message = Sanitize(ex.Message)
            };
        }
    }

    private static GraphSendMailRequest BuildSendMailPayload(
        EmailAccountCredentials credentials,
        EmailDeliveryRequest request)
    {
        var attachments = new List<GraphFileAttachment>();
        foreach (var attachment in request.Attachments)
        {
            attachments.Add(new GraphFileAttachment
            {
                ODataType = "#microsoft.graph.fileAttachment",
                Name = attachment.FileName,
                ContentType = attachment.ContentType,
                ContentBytes = Convert.ToBase64String(attachment.Content)
            });
        }

        return new GraphSendMailRequest
        {
            Message = new GraphMessage
            {
                Subject = request.Subject,
                Body = new GraphItemBody
                {
                    ContentType = "HTML",
                    Content = request.HtmlBody
                },
                ToRecipients =
                [
                    new GraphRecipient
                    {
                        EmailAddress = new GraphEmailAddress { Address = request.ToEmail }
                    }
                ],
                From = new GraphRecipient
                {
                    EmailAddress = new GraphEmailAddress
                    {
                        Address = credentials.EmailAddress,
                        Name = credentials.DisplayName
                    }
                },
                Attachments = attachments
            },
            SaveToSentItems = true
        };
    }

    private static bool IsTransientStatus(int statusCode)
    {
        return statusCode == 429 || statusCode == 503 || statusCode == 504;
    }

    private static string MapGraphError(int statusCode, string body)
    {
        if (statusCode == 401 || statusCode == 403)
        {
            return "Microsoft account authorization failed. Reconnect the account and grant Mail.Send permission.";
        }

        if (statusCode == 429)
        {
            return "Microsoft Graph rate limit reached. Try again shortly.";
        }

        if (statusCode == 404)
        {
            return "Microsoft mailbox was not found for this account.";
        }

        if (Contains(body, "ErrorInvalidRecipients") || Contains(body, "ErrorInvalidMailbox"))
        {
            return "The recipient address is invalid.";
        }

        if (Contains(body, "ErrorAttachmentSizeLimitExceeded") || Contains(body, "ErrorMessageSizeExceeded"))
        {
            return "Attachment was rejected by Microsoft Graph because it is too large.";
        }

        return $"Microsoft Graph send failed ({statusCode}).";
    }

    private static bool Contains(string body, string value)
    {
        return !string.IsNullOrWhiteSpace(body)
            && body.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Microsoft Graph send failed.";
        }

        return message.Length > 500 ? message[..500] : message;
    }

    private sealed class GraphSendMailRequest
    {
        [JsonPropertyName("message")]
        public required GraphMessage Message { get; init; }

        [JsonPropertyName("saveToSentItems")]
        public bool SaveToSentItems { get; init; }
    }

    private sealed class GraphMessage
    {
        [JsonPropertyName("subject")]
        public required string Subject { get; init; }

        [JsonPropertyName("body")]
        public required GraphItemBody Body { get; init; }

        [JsonPropertyName("toRecipients")]
        public required List<GraphRecipient> ToRecipients { get; init; }

        [JsonPropertyName("from")]
        public GraphRecipient? From { get; init; }

        [JsonPropertyName("attachments")]
        public List<GraphFileAttachment>? Attachments { get; init; }
    }

    private sealed class GraphItemBody
    {
        [JsonPropertyName("contentType")]
        public required string ContentType { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    private sealed class GraphRecipient
    {
        [JsonPropertyName("emailAddress")]
        public required GraphEmailAddress EmailAddress { get; init; }
    }

    private sealed class GraphEmailAddress
    {
        [JsonPropertyName("address")]
        public required string Address { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed class GraphFileAttachment
    {
        [JsonPropertyName("@odata.type")]
        public required string ODataType { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("contentType")]
        public required string ContentType { get; init; }

        [JsonPropertyName("contentBytes")]
        public required string ContentBytes { get; init; }
    }
}
