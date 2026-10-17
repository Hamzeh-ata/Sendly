using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Sendly.Infrastructure.Persistence;

public sealed class UppercaseGuidConverter : ValueConverter<Guid, string>
{
    public UppercaseGuidConverter()
        : base(
            guid => guid.ToString("D").ToUpperInvariant(),
            value => Guid.Parse(value))
    {
    }
}

public sealed class UppercaseNullableGuidConverter : ValueConverter<Guid?, string?>
{
    public UppercaseNullableGuidConverter()
        : base(
            guid => guid.HasValue ? guid.Value.ToString("D").ToUpperInvariant() : null,
            value => string.IsNullOrWhiteSpace(value) ? null : Guid.Parse(value))
    {
    }
}
