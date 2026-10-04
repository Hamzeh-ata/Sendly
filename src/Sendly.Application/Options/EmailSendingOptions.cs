namespace Sendly.Application.Options;

public class EmailSendingOptions
{
    public const string SectionName = "EmailSending";

    public int DefaultDelayMilliseconds { get; set; } = 1000;

    public int DefaultBatchSize { get; set; } = 50;

    public int DefaultMaxRetryCount { get; set; } = 2;

    public int ConnectionTimeoutSeconds { get; set; } = 30;
}
