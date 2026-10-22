namespace Demo.Api.AI;

// Parse locally: an invalid optional feature must not prevent ERP startup.
public sealed class AiAssistantOptions
{
    public bool Enabled { get; init; }
    public string Provider { get; init; } = "OpenAI";
    public string Model { get; init; } = "gpt-4.1-mini-2025-04-14";
    public string Endpoint { get; init; } = "https://api.openai.com/v1";
    public string OllamaEndpoint { get; init; } = "http://127.0.0.1:11434";
    public string ApiKey { get; init; } = "";
    public int OllamaContextWindow { get; init; } = 8192;
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxOutputTokens { get; init; } = 1000;
    public int MaxToolCalls { get; init; } = 5;
    public int RequestsPerMinute { get; init; } = 10;
    public int RequestsPerDay { get; init; } = 100;
    public int MaxRetries { get; init; } = 1;
    public int RetentionDays { get; init; } = 7;
    public bool AllowDevelopmentMock { get; init; }
    public bool Configured => ((Provider == "OpenAI" && ApiKey.Length is >= 10 and <= 512 && Endpoint == "https://api.openai.com/v1")
        || (Provider == "Mock" && AllowDevelopmentMock)
        || (Provider == "Ollama" && AllowDevelopmentMock && IsLocalOllamaEndpoint(OllamaEndpoint) && OllamaContextWindow is >= 2048 and <= 16384))
        && !ApiKey.Any(char.IsWhiteSpace) && System.Text.RegularExpressions.Regex.IsMatch(Model, @"^[a-zA-Z0-9][a-zA-Z0-9._:/\-]{0,99}$")
        && MaxRetries is >= 0 and <= 2 && RetentionDays is >= 1 and <= 30 && TimeoutSeconds is >= 1 and <= 120
        && MaxOutputTokens is >= 100 and <= 2000 && MaxToolCalls is >= 1 and <= 5
        && RequestsPerMinute is >= 1 and <= 60 && RequestsPerDay is >= 1 and <= 1000;
    // Exact numeric loopback avoids DNS rebinding; never accept arbitrary hosts, credentials or paths.
    public static bool IsLocalOllamaEndpoint(string value) => value == "http://127.0.0.1:11434";
    public static AiAssistantOptions Read(IConfiguration config, bool development = false)
    {
        var c = config.GetSection("AiAssistant");
        int Number(string key, int fallback) => c[key] is null ? fallback : int.TryParse(c[key], out var n) ? n : 0;
        string First(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
        var provider = c["Provider"] ?? "OpenAI";
        return new() { AllowDevelopmentMock = development, MaxRetries = Number("MaxRetries", 1), RetentionDays = Number("RetentionDays", 7), Enabled = bool.TryParse(c["Enabled"], out var enabled) && enabled,
            Provider = provider, Model = provider == "Ollama" ? First(config["OLLAMA_MODEL"], "qwen3:8b") : First(config["OPENAI_MODEL"], c["Model"], "gpt-4.1-mini-2025-04-14"),
            OllamaEndpoint = First(config["OLLAMA_ENDPOINT"], "http://127.0.0.1:11434"),
            OllamaContextWindow = int.TryParse(config["OLLAMA_CONTEXT_WINDOW"] ?? "8192", out var context) ? context : 0,
            Endpoint = c["Endpoint"] ?? "https://api.openai.com/v1", ApiKey = provider == "Ollama" ? "" : First(config["OPENAI_API_KEY"], c["ApiKey"]),
            TimeoutSeconds = Number("TimeoutSeconds", 30), MaxOutputTokens = Number("MaxOutputTokens", 1000),
            MaxToolCalls = Number("MaxToolCalls", 5), RequestsPerMinute = Number("RequestsPerMinute", 10), RequestsPerDay = Number("RequestsPerDay", 100) };
    }
}
