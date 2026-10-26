namespace Sendly.Application.DTOs;

public class GeoCountryDto
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string> Cities { get; set; } = [];
}
