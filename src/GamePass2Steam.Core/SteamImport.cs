using System.Runtime.ExceptionServices;

namespace GamePass2Steam.Core;

public static class SteamImport
{
    public static async Task Run(bool authorized, Func<bool> running, Action requestExit, Action restart,
        Action import, CancellationToken cancellation = default, Func<Task>? wait = null)
    {
        bool wasRunning = running();
        if (wasRunning && !authorized) throw new IOException("Exit Steam manually before importing.");
        // Authorization refers to the client that was running when the GUI offered this operation.
        bool reopen = authorized;
        Exception? failure = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (wasRunning)
            {
                requestExit();
                // Even after cancellation, observe shutdown so Steam can be reopened safely.
                for (int attempt = 0; running() && attempt < 120; attempt++)
                    await (wait?.Invoke() ?? Task.Delay(500));
                if (running()) throw new IOException("Steam did not close within 60 seconds. Nothing was imported. If Steam finishes closing later, reopen it manually.");
            }
            cancellation.ThrowIfCancellationRequested();
            if (running()) throw new IOException("Steam restarted before the import. Nothing was imported.");
            import();
        }
        catch (Exception e) { failure = e; }
        if (reopen && !running())
        {
            try { restart(); }
            catch (Exception e)
            {
                failure = new IOException("Steam could not be reopened. Open Steam manually." + (failure == null ? " The shortcut operation completed." : " The shortcut operation also failed: " + failure.Message), failure == null ? e : new AggregateException(failure, e));
            }
        }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
