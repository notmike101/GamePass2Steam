namespace GamePass2Steam.Core;

public enum SessionState { Starting, Running, Exited, Failed }

public sealed class SessionTracker
{
    public static bool IsRunning(string kind, bool matchingProcess, bool visibleWindow) => matchingProcess && (kind != "UWP" || visibleWindow);
    private bool seen;
    private TimeSpan? absentSince;
    public SessionState Observe(TimeSpan elapsed, bool gameRunning)
    {
        if (gameRunning) { seen = true; absentSince = null; return SessionState.Running; }
        if (!seen) return elapsed >= TimeSpan.FromSeconds(120) ? SessionState.Failed : SessionState.Starting;
        absentSince ??= elapsed;
        return elapsed - absentSince >= TimeSpan.FromSeconds(3) ? SessionState.Exited : SessionState.Running;
    }
}
