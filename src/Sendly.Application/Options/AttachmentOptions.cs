namespace Sendly.Application.Options;

public class AttachmentOptions
{
    public const string SectionName = "Attachments";

    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;

    public long MaxTotalSizeBytes { get; set; } = 20 * 1024 * 1024;

    public string[] AllowedContentTypes { get; set; } =
    [
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "image/png",
        "image/jpeg",
        "image/gif",
        "text/plain",
        "text/csv",
        "application/zip"
    ];

    public string StorageRoot { get; set; } = "App_Data/attachments";
}
