namespace CCP.Classic.Core;

// A native listener and routed window events can arrive in different orders for the same key.
// When the listener is available it owns both edges; local events are the fallback only.
public sealed class PanicInputRouter(SessionController session, Func<bool> globalAvailable)
{
    public bool GlobalAvailable => globalAvailable();
    public bool ConsumesKey(string key) => session.ConsumesKey(key);
    public bool LocalDown(string key) => !globalAvailable() && session.KeyDown(key);
    public void LocalUp(string key) { if (!globalAvailable()) session.KeyUp(key); }
    public bool GlobalChanged(string key, bool down)
    {
        if (down) return session.KeyDown(key);
        session.KeyUp(key);
        return false;
    }
}
