using Sendly.Domain.Services;

namespace Sendly.Domain.Tests;

public class EmailAddressParserTests
{
    [Fact]
    public void Parse_SupportsLineCommaAndSemicolonSeparators()
    {
        var result = EmailAddressParser.Parse("one@example.com\ntwo@example.com, three@example.com; four@example.com");

        Assert.Equal(4, result.ValidCount);
        Assert.Contains("one@example.com", result.ValidEmails);
        Assert.Contains("four@example.com", result.ValidEmails);
    }

    [Fact]
    public void Parse_TrimsAndRemovesDuplicates()
    {
        var result = EmailAddressParser.Parse("  a@example.com \nA@example.com; b@example.com ");

        Assert.Equal(2, result.ValidCount);
        Assert.Equal("a@example.com", result.ValidEmails[0]);
        Assert.Equal("b@example.com", result.ValidEmails[1]);
    }

    [Fact]
    public void Parse_CollectsInvalidAddresses()
    {
        var result = EmailAddressParser.Parse("good@example.com\nnot-an-email\nbad@");

        Assert.Equal(1, result.ValidCount);
        Assert.Equal(2, result.InvalidCount);
    }
}
