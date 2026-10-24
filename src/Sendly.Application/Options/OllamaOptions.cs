namespace Sendly.Application.Options;

public class OllamaOptions
{
    public const string SectionName = "Ollama";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "qwen3:4b";

    public int TimeoutSeconds { get; set; } = 60;

    public int MaxRetries { get; set; } = 1;

    public int MaxInputCharacters { get; set; } = 6000;

    public int MaxOutputTokens { get; set; } = 512;

    public int MaxQueriesPerRequest { get; set; } = 12;
}
