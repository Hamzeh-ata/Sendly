using System.ComponentModel.DataAnnotations;

namespace Sendly.Domain.Entities;

public class SignatureSettings
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public bool SignaturesEnabled { get; set; } = true;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
