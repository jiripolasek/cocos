using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace JPSoftworks.Cocos.Services.Chat;

internal sealed class OpenAiChatService : IChatService
{
    private readonly HttpClient _httpClient = new();
    private readonly ILogger<OpenAiChatService> _logger;

    public OpenAiChatService(ILogger<OpenAiChatService> logger)
    {
        this._logger = logger;
    }

    public string Provider => ChatProviders.OpenAI;

    public async Task<string> GetCompletionAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint))
        {
            throw new InvalidOperationException("OpenAI endpoint is not configured.");
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new InvalidOperationException("OpenAI model is not configured.");
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not configured.");
        }

        var endpoint = EnsureEndpoint(request.Endpoint, "/v1/chat/completions");
        var payload = new OpenAiChatRequest
        {
            Model = request.Model,
            Messages = BuildMessages(request)
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
        message.Content = JsonContent.Create(payload);

        this._logger.LogDebug("Sending OpenAI request to {Endpoint} with model {Model}.", endpoint, request.Model);
        using var response = await this._httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: cancellationToken);
        var content = data?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("OpenAI response did not include content.");
        }

        return content.Trim();
    }

    private static string EnsureEndpoint(string endpoint, string suffix)
    {
        var trimmed = endpoint.Trim();
        if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"{trimmed.TrimEnd('/')}{suffix}";
    }

    private static List<OpenAiChatMessage> BuildMessages(ChatRequest request)
    {
        var messages = new List<OpenAiChatMessage>();
        var systemPrompt = request.GetEffectiveSystemPrompt();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new OpenAiChatMessage("system", systemPrompt));
        }

        messages.Add(new OpenAiChatMessage("user", request.Prompt));
        return messages;
    }

    private sealed record OpenAiChatRequest
    {
        public required string Model { get; init; }

        public required List<OpenAiChatMessage> Messages { get; init; }
    }

    private sealed record OpenAiChatMessage(string Role, string Content);

    private sealed record OpenAiChatChoice(OpenAiChatMessage Message);

    private sealed record OpenAiChatResponse(IReadOnlyList<OpenAiChatChoice> Choices);
}
