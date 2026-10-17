using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.WhatsApp;

public class GraphWhatsAppSender : IWhatsAppSender
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WhatsAppBusinessOptions _options;
    private readonly ILogger<GraphWhatsAppSender> _logger;

    public GraphWhatsAppSender(
        IHttpClientFactory httpClientFactory,
        IOptions<WhatsAppBusinessOptions> options,
        ILogger<GraphWhatsAppSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<WhatsAppSendResult> SendAsync(WhatsAppSendRequest request, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return new WhatsAppSendResult
            {
                Success = false,
                Message = "WhatsApp Business API is not configured."
            };
        }

        if (string.IsNullOrWhiteSpace(request.ToNumber))
        {
            return new WhatsAppSendResult
            {
                Success = false,
                Message = "WhatsApp recipient number is required."
            };
        }

        var useTemplate = request.PreferTemplate
            && _options.PreferTemplateMessages
            && !string.IsNullOrWhiteSpace(request.TemplateName);

        try
        {
            var client = _httpClientFactory.CreateClient("WhatsAppBusiness");
            var path = $"{_options.ApiVersion.Trim('/')}/{_options.PhoneNumberId}/messages";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

            object payload = useTemplate
                ? BuildTemplatePayload(request)
                : BuildTextPayload(request);

            httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WhatsApp send failed with status {Status}", (int)response.StatusCode);
                if (useTemplate && !string.IsNullOrWhiteSpace(request.MessageBody))
                {
                    return await SendTextFallbackAsync(client, path, request, cancellationToken);
                }

                return new WhatsAppSendResult
                {
                    Success = false,
                    Message = $"WhatsApp provider rejected the message ({(int)response.StatusCode})."
                };
            }

            return new WhatsAppSendResult
            {
                Success = true,
                Message = useTemplate
                    ? "WhatsApp template message accepted by provider."
                    : "WhatsApp message accepted by provider.",
                ProviderMessageId = ExtractMessageId(body)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp send failed");
            return new WhatsAppSendResult
            {
                Success = false,
                Message = "WhatsApp send failed due to a provider error."
            };
        }
    }

    private async Task<WhatsAppSendResult> SendTextFallbackAsync(
        HttpClient client,
        string path,
        WhatsAppSendRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(BuildTextPayload(request)),
            Encoding.UTF8,
            "application/json");
        using var response = await client.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new WhatsAppSendResult
            {
                Success = false,
                Message = $"WhatsApp provider rejected the fallback text message ({(int)response.StatusCode})."
            };
        }

        return new WhatsAppSendResult
        {
            Success = true,
            Message = "WhatsApp text fallback message accepted by provider.",
            ProviderMessageId = ExtractMessageId(body)
        };
    }

    private object BuildTemplatePayload(WhatsAppSendRequest request)
    {
        var language = string.IsNullOrWhiteSpace(request.TemplateLanguage)
            ? _options.DefaultTemplateLanguage
            : request.TemplateLanguage;

        var components = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.TemplateHeaderParameter))
        {
            components.Add(new
            {
                type = "header",
                parameters = new object[]
                {
                    new { type = "text", text = request.TemplateHeaderParameter }
                }
            });
        }

        if (request.TemplateBodyParameters.Count > 0)
        {
            var parameters = new List<object>();
            foreach (var value in request.TemplateBodyParameters)
            {
                parameters.Add(new { type = "text", text = value });
            }

            components.Add(new { type = "body", parameters });
        }

        if (!string.IsNullOrWhiteSpace(request.TemplateButtonParameter))
        {
            var subType = string.IsNullOrWhiteSpace(request.TemplateButtonSubType)
                ? "url"
                : request.TemplateButtonSubType.Trim().ToLowerInvariant();
            components.Add(new
            {
                type = "button",
                sub_type = subType,
                index = (request.TemplateButtonIndex ?? 0).ToString(),
                parameters = new object[]
                {
                    new { type = "text", text = request.TemplateButtonParameter }
                }
            });
        }

        return new
        {
            messaging_product = "whatsapp",
            to = DigitsOnly(request.ToNumber),
            type = "template",
            template = new
            {
                name = request.TemplateName,
                language = new { code = language },
                components
            }
        };
    }

    private static object BuildTextPayload(WhatsAppSendRequest request)
    {
        return new
        {
            messaging_product = "whatsapp",
            to = DigitsOnly(request.ToNumber),
            type = "text",
            text = new { preview_url = false, body = request.MessageBody }
        };
    }

    private static string? ExtractMessageId(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("messages", out var messages)
            && messages.ValueKind == JsonValueKind.Array
            && messages.GetArrayLength() > 0
            && messages[0].TryGetProperty("id", out var idProp))
        {
            return idProp.GetString();
        }

        return null;
    }

    private static string DigitsOnly(string value)
    {
        return new string(value.Where(char.IsDigit).ToArray());
    }
}
