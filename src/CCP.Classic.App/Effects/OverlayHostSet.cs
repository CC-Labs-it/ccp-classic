using Avalonia;

namespace CCP.Classic.App.Effects;

internal readonly record struct MonitorTarget(PixelRect Bounds, double Scaling);

// Retain hosts by physical layout so changing settings or display order does not restart effects.
internal sealed class OverlayHostSet<TKey, THost> where TKey : notnull where THost : class
{
    private readonly Dictionary<TKey, THost> hosts = [];
    public IEnumerable<THost> Hosts => hosts.Values;

    public bool Reconcile(IEnumerable<TKey> selected, Func<TKey, THost?> create, Action<THost> close)
    {
        var wanted = selected.ToHashSet();
        foreach (var key in hosts.Keys.Where(key => !wanted.Contains(key)).ToArray())
        {
            close(hosts[key]);
            hosts.Remove(key);
        }
        foreach (var key in wanted)
        {
            if (hosts.ContainsKey(key)) continue;
            var host = create(key);
            if (host is null) { Clear(close); return false; }
            hosts.Add(key, host);
        }
        return hosts.Count > 0;
    }

    public void Clear(Action<THost> close)
    {
        foreach (var host in hosts.Values) close(host);
        hosts.Clear();
    }
}
