namespace SpireStrategist.Core;

// Decisions must remain current during the delay. A submitted action is never retried silently.
public sealed class ActionGate
{
    public bool Enabled { get; private set; }
    private string? pending;
    private ulong since;
    private readonly HashSet<string> submitted = [];
    public void Enable() { Enabled = true; ResetPending(); }
    public void Pause() { Enabled = false; ResetPending(); }
    public void ResetPending() { pending = null; since = 0; }
    public bool WasSubmitted(string key)=>submitted.Contains(key);
    public bool TryClaim(string key, ulong now, bool ready, ulong delay = 2000)
    {
        if (!Enabled || !ready) { ResetPending(); return false; }
        if (submitted.Contains(key)) return false;
        if (pending != key) { pending = key; since = now; return false; }
        if (now < since || now - since < delay) return false;
        submitted.Add(key);
        ResetPending();
        return true;
    }
}
