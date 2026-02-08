namespace JPSoftworks.Cocos.Services.Chat;

internal sealed record ChatRequest
{
    /// <summary>
    /// Instruction appended to every system prompt to enforce structured output.
    /// </summary>
    internal const string ResultTagInstruction =
        "IMPORTANT: You MUST wrap your final answer or deliverable in <result></result> XML tags. " +
        "Explanations, reasoning, and commentary go OUTSIDE the tags. " +
        "The content inside <result></result> will be extracted automatically.\n" +
        "Example:\n" +
        "Sure, here is the translation:\n" +
        "<result>\nBonjour le monde\n</result>";

    public required string Prompt { get; init; }

    public string SystemPrompt { get; init; } = string.Empty;

    public required string Model { get; init; }

    public required string Endpoint { get; init; }

    public string? ApiKey { get; init; }

    public IReadOnlyList<ChatContextItem> ContextItems { get; init; } = Array.Empty<ChatContextItem>();

    /// <summary>
    /// Returns the system prompt with the result-tag instruction appended.
    /// </summary>
    public string GetEffectiveSystemPrompt()
    {
        if (string.IsNullOrWhiteSpace(this.SystemPrompt))
        {
            return ResultTagInstruction;
        }

        return $"{this.SystemPrompt}\n\n{ResultTagInstruction}";
    }
}

internal sealed record ChatContextItem(string Label, string Content);
