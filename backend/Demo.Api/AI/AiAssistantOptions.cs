namespace Demo.Api.AI;

// Parse locally: an invalid optional feature must not prevent ERP startup.
public sealed class AiAssistantOptions
{
    public bool Enabled { get; init; }
    public string Provider { get; init; } = "OpenAI";
    public string Model { get; init; } = "gpt-4.1-mini-2025-04-14";
    public string Endpoint { get; init; } = "https://api.openai.com/v1";
    public string ApiKey { get; init; } = "";
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxOutputTokens { get; init; } = 1000;
    public int MaxToolCalls { get; init; } = 5;
    public int RequestsPerMinute { get; init; } = 10;
    public int RequestsPerDay { get; init; } = 100;
    public bool Configured => Provider == "OpenAI" && ApiKey.Length is >= 10 and <= 512
        && System.Text.RegularExpressions.Regex.IsMatch(Model, @"^[a-zA-Z0-9.\-]{1,100}$")
        && Endpoint == "https://api.openai.com/v1" && TimeoutSeconds is >= 1 and <= 120
        && MaxOutputTokens is >= 100 and <= 2000 && MaxToolCalls is >= 1 and <= 5
        && RequestsPerMinute is >= 1 and <= 60 && RequestsPerDay is >= 1 and <= 1000;
    public static AiAssistantOptions Read(IConfiguration config)
    {
        var c = config.GetSection("AiAssistant");
        int Number(string key, int fallback) => c[key] is null ? fallback : int.TryParse(c[key], out var n) ? n : 0;
        return new() { Enabled = bool.TryParse(c["Enabled"], out var enabled) && enabled,
            Provider = c["Provider"] ?? "OpenAI", Model = c["Model"] ?? "gpt-4.1-mini-2025-04-14",
            Endpoint = c["Endpoint"] ?? "https://api.openai.com/v1", ApiKey = c["ApiKey"] ?? "",
            TimeoutSeconds = Number("TimeoutSeconds", 30), MaxOutputTokens = Number("MaxOutputTokens", 1000),
            MaxToolCalls = Number("MaxToolCalls", 5), RequestsPerMinute = Number("RequestsPerMinute", 10), RequestsPerDay = Number("RequestsPerDay", 100) };
    }
}
