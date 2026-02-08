using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace JPSoftworks.Cocos.Services.Chat;

internal sealed class OllamaChatService : IChatService
{
    private readonly HttpClient _httpClient = new();
    private readonly ILogger<OllamaChatService> _logger;

    public OllamaChatService(ILogger<OllamaChatService> logger)
    {
        this._logger = logger;
    }

    public string Provider => ChatProviders.Ollama;

    public async Task<string> GetCompletionAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint))
        {
            throw new InvalidOperationException("Ollama endpoint is not configured.");
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new InvalidOperationException("Ollama model is not configured.");
        }

        var endpoint = EnsureEndpoint(request.Endpoint, "/api/chat");
        var payload = new OllamaChatRequest
        {
            Model = request.Model,
            Stream = false,
            Messages = BuildMessages(request)
        };

        this._logger.LogDebug("Sending Ollama request to {Endpoint} with model {Model}.", endpoint, request.Model);
        using var response = await this._httpClient.PostAsJsonAsync(endpoint, payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken);
        var content = data?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Ollama response did not include content.");
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

    private static List<OllamaChatMessage> BuildMessages(ChatRequest request)
    {
        var messages = new List<OllamaChatMessage>();
        var systemPrompt = request.GetEffectiveSystemPrompt();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new OllamaChatMessage("system", systemPrompt));
        }

        messages.Add(new OllamaChatMessage("user", request.Prompt));
        return messages;
    }

    private sealed record OllamaChatRequest
    {
        public required string Model { get; init; }

        public bool Stream { get; init; }

        public required List<OllamaChatMessage> Messages { get; init; }
    }

    private sealed record OllamaChatMessage(string Role, string Content);

    private sealed record OllamaChatResponse(OllamaChatMessage? Message);
}
