using CCP.Classic.Core.LocalDevices;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LocalDeviceTests
{
    [Theory]
    [InlineData("localhost", "127.0.0.1")]
    [InlineData("LOCALHOST", "127.0.0.1")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("::1", "[::1]")]
    public void OnlyLiteralLoopbackEndpointsAreCreated(string host, string expectedHost)
    {
        var intiface = LocalDeviceEndpoint.Create(LocalDeviceProvider.Intiface, 12345, host);
        var lovense = LocalDeviceEndpoint.Create(LocalDeviceProvider.LovenseConnect, 20010, host);
        Assert.Equal(expectedHost, intiface.Host); Assert.Equal("ws", intiface.Scheme);
        Assert.Equal("/", intiface.AbsolutePath);
        Assert.Equal("http", lovense.Scheme); Assert.Equal("/command", lovense.AbsolutePath);
        Assert.Equal(intiface, LocalDeviceEndpoint.Validate(intiface));
        Assert.Equal(lovense, LocalDeviceEndpoint.Validate(lovense));
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("example.invalid")]
    [InlineData("localhost.example.invalid")]
    [InlineData("127-0-0-1.lovense.club")]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1@example.invalid")]
    [InlineData("")]
    public void HostnamesLanAliasesAndWildcardInterfacesAreRejected(string host) =>
        Assert.Throws<ArgumentException>(() => LocalDeviceEndpoint.Create(LocalDeviceProvider.Intiface, 12345, host));

    [Theory]
    [InlineData("http://example.invalid:20010/command")]
    [InlineData("ws://192.168.1.1:12345/")]
    [InlineData("ws://user@127.0.0.1:12345/")]
    [InlineData("ws://127.0.0.1:12345/?redirect=http://example.invalid")]
    [InlineData("http://127.0.0.1:20010/other")]
    [InlineData("https://127.0.0.1:20010/command")]
    public async Task InvalidTransportEndpointsFailBeforeAnyConnection(string endpoint)
    {
        await using var transport = new LoopbackJsonTransport();
        await Assert.ThrowsAsync<ArgumentException>(() => transport.OpenAsync(new Uri(endpoint), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void PortsMustFitTheLocalTcpRange(int port) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalDeviceEndpoint.Create(LocalDeviceProvider.Intiface, port));
}
