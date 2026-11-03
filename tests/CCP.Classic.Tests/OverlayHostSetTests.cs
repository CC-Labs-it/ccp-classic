using Avalonia;
using CCP.Classic.App.Effects;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class OverlayHostSetTests
{
    private static readonly MonitorTarget Left = new(new PixelRect(0, 0, 1920, 1080), 1);
    private static readonly MonitorTarget Right = new(new PixelRect(1920, 0, 1920, 1080), 1);
    private readonly OverlayHostSet<MonitorTarget, Host> hosts = new();
    private readonly List<Host> created = [];
    private Host Create(MonitorTarget target) { var host = new Host(target); created.Add(host); return host; }
    private static void Close(Host host) => host.Closed = true;
    private sealed class Host(MonitorTarget target)
    {
        public MonitorTarget Target { get; } = target;
        public bool Closed { get; set; }
    }

    [Fact]
    public void SwitchingFromAllToCurrentAndBackRetainsTheCurrentHost()
    {
        Assert.True(hosts.Reconcile([Left, Right], Create, Close));
        var retained = created.Single(host => host.Target == Right);
        Assert.True(hosts.Reconcile([Right], Create, Close));
        Assert.Same(retained, Assert.Single(hosts.Hosts));
        Assert.True(created.Single(host => host.Target == Left).Closed);
        Assert.True(hosts.Reconcile([Left, Right], Create, Close));
        Assert.Contains(retained, hosts.Hosts);
        Assert.Equal(3, created.Count);
        Assert.False(retained.Closed);
    }

    [Fact]
    public void MovingTheOwnerOrChangingDisplayGeometryReplacesOnlyAffectedHosts()
    {
        hosts.Reconcile([Left], Create, Close);
        hosts.Reconcile([Right], Create, Close);
        Assert.True(created[0].Closed);
        var scaled = Right with { Scaling = 1.5 };
        hosts.Reconcile([scaled], Create, Close);
        Assert.True(created[1].Closed);
        Assert.Equal(scaled, Assert.Single(hosts.Hosts).Target);
        var resized = scaled with { Bounds = new PixelRect(1920, 0, 2560, 1440) };
        hosts.Reconcile([resized], Create, Close);
        Assert.True(created[2].Closed);
        Assert.Equal(resized, Assert.Single(hosts.Hosts).Target);
    }

    [Fact]
    public void ReorderedEquivalentDisplaysDoNotRecreateHostsAndRemovalClosesThem()
    {
        hosts.Reconcile([Left, Right], Create, Close);
        hosts.Reconcile([Right with { }, Left with { }], Create, Close);
        Assert.Equal(2, created.Count);
        Assert.All(created, host => Assert.False(host.Closed));
        Assert.False(hosts.Reconcile([], Create, Close));
        Assert.Empty(hosts.Hosts);
        Assert.All(created, host => Assert.True(host.Closed));
    }

    [Fact]
    public void FailedNewDisplayClosesRetainedHostsAndStopsReconciliation()
    {
        hosts.Reconcile([Left], Create, Close);
        Assert.False(hosts.Reconcile([Left, Right], _ => null, Close));
        Assert.Empty(hosts.Hosts);
        Assert.True(created[0].Closed);
    }
}
