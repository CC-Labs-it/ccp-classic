using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class PanicInputRouterTests
{
    [Fact]
    public void PanicConsumptionIsIndependentOfNativeDeliveryAndRespectsTheGate()
    {
        using var session = new SessionController(new() { PanicKey = "Space" });
        var input = new PanicInputRouter(session, () => true);
        Assert.False(input.LocalDown("Space"));
        Assert.True(input.ConsumesKey("Space"));
        Assert.False(input.ConsumesKey("Escape"));
        session.SetPanicEnabled(false, true);
        Assert.False(input.ConsumesKey("Space"));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeAndWindowEventsCannotCountTheSamePressTwice(bool nativeFirst)
    {
        using var session = new SessionController(new());
        var input = new PanicInputRouter(session, () => true);
        session.Start();
        if (nativeFirst) Assert.True(input.GlobalChanged("Escape", true));
        Assert.False(input.LocalDown("Escape"));
        input.LocalUp("Escape");
        if (!nativeFirst) Assert.True(input.GlobalChanged("Escape", true));
        session.Start();
        Assert.False(input.GlobalChanged("Escape", true));
        Assert.True(session.IsRunning);
        Assert.False(input.GlobalChanged("Escape", false));
        Assert.True(input.GlobalChanged("Escape", true));
        Assert.False(session.IsRunning);
    }

    [Fact]
    public void LocalEventsWorkWhenTheNativeListenerIsUnavailable()
    {
        using var session = new SessionController(new());
        var input = new PanicInputRouter(session, () => false);
        session.Start();
        Assert.True(input.LocalDown("Escape"));
        session.Start();
        Assert.False(input.LocalDown("Escape"));
        input.LocalUp("Escape");
        Assert.True(input.LocalDown("Escape"));
    }
}
