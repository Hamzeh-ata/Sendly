namespace Sendly.Application.Options;

public class BackgroundQueueOptions
{
    public const string SectionName = "BackgroundQueues";

    public string Provider { get; set; } = "InMemory";

    public bool UseDurableQueue =>
        string.Equals(Provider, "Ef", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Provider, "Durable", StringComparison.OrdinalIgnoreCase);
}
