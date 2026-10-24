using Sendly.Application.Services;
var d = UrlAdmissionPolicy.Evaluate("https://example-dir.org/us/schools", countries: new[] { "United States" });
Console.WriteLine($"{d.Accepted} {d.ReasonCode} {d.Explanation}");
var d2 = UrlAdmissionPolicy.Evaluate("https://www.nais.org/school-directory", countries: new[] { "United States" });
Console.WriteLine($"{d2.Accepted} {d2.ReasonCode} {d2.Explanation}");
