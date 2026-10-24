using System.Net;
using Sendly.Domain.Services;

namespace Sendly.Domain.Tests;

public class SsrfGuardTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.1.1")]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("metadata.google.internal")]
    [InlineData("::1")]
    public void IsBlockedHost_blocks_private_and_metadata_hosts(string host)
    {
        Assert.True(SsrfGuard.IsBlockedHost(host));
    }

    [Fact]
    public void IsBlockedAddress_blocks_loopback()
    {
        Assert.True(SsrfGuard.IsBlockedAddress(IPAddress.Loopback));
        Assert.True(SsrfGuard.IsBlockedAddress(IPAddress.Parse("127.0.1.1")));
        Assert.True(SsrfGuard.IsBlockedAddress(IPAddress.IPv6Loopback));
        Assert.True(SsrfGuard.IsBlockedAddress(IPAddress.Parse("::ffff:127.0.0.1")));
        Assert.True(SsrfGuard.IsBlockedAddress(IPAddress.Parse("fc00::1")));
    }

    [Fact]
    public void IsBlockedHost_allows_public_hostnames()
    {
        Assert.False(SsrfGuard.IsBlockedHost("example.com"));
    }
}
