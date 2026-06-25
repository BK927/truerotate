using Microsoft.UI.Dispatching;

namespace TrueRotate;

/// <summary>
/// Watches for WM_DISPLAYCHANGE events and re-applies the user's desired
/// orientations whenever an external agent (e.g. the NVIDIA app) resets them.
///
/// Idempotency / loop safety:
///   Reconcile() only calls SetRotation when rotation != desired. Our own
///   SetRotation causes another WM_DISPLAYCHANGE → debounce → Reconcile →
///   nothing to fix → loop stops. No timer masking is needed.
///
/// Anti-thrash:
///   If ≥5 corrective calls happen within 10 s without a clean Reconcile in
///   between, auto-reapply is paused for that window and one balloon is shown.
///   It resets once Reconcile finds everything already correct.
/// </summary>
internal sealed class ReapplyController : IDisposable
{
    private const int DebounceMs     = 400;
    private const int ThrashLimit    = 5;
    private const int ThrashWindowMs = 10_000;

    private readonly OrientationStore       _store;
    private readonly Action<string, string> _showWarning;   // (title, text)
    private readonly DispatcherQueueTimer   _debounce;

    // Anti-thrash state
    private int      _correctiveCount;
    private DateTime _firstCorrectiveTime = DateTime.MinValue;
    private bool     _thrashPaused;

    public ReapplyController(OrientationStore store, Action<string, string> showWarning)
    {
        _store       = store;
        _showWarning = showWarning;

        // DispatcherQueueTimer fires on the UI thread (same as WinForms Timer.Tick)
        var dq = DispatcherQueue.GetForCurrentThread();
        _debounce = dq.CreateTimer();
        _debounce.Interval = TimeSpan.FromMilliseconds(DebounceMs);
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => Reconcile();
    }

    /// <summary>Called by HotkeyWindow when WM_DISPLAYCHANGE arrives.</summary>
    public void OnDisplayChange()
    {
        // Restart the debounce; coalesces rapid NVIDIA event bursts.
        _debounce.Stop();
        _debounce.Start();
    }

    private void Reconcile()
    {
        if (!_store.AutoReapply) return;

        var desired = _store.All();
        if (desired.Count == 0) return;

        List<MonitorInfo> monitors;
        try { monitors = DisplayService.EnumerateMonitors(); }
        catch { return; }  // Can't enumerate; skip this cycle.

        bool anyMismatch = false;

        foreach (var mon in monitors)
        {
            if (!desired.TryGetValue(mon.DevicePath, out uint want)) continue;
            if (mon.Rotation == want) continue;

            anyMismatch = true;

            // While thrash-paused, keep detecting (so the settled-state reset
            // below can self-heal once the display stops reverting) but stop
            // issuing corrections.
            if (_thrashPaused) continue;

            // Anti-thrash accounting
            var now = DateTime.UtcNow;
            if (_firstCorrectiveTime == DateTime.MinValue
                || (now - _firstCorrectiveTime).TotalMilliseconds > ThrashWindowMs)
            {
                _firstCorrectiveTime = now;
                _correctiveCount     = 0;
            }

            _correctiveCount++;

            if (_correctiveCount > ThrashLimit)
            {
                _thrashPaused = true;
                _showWarning(
                    L.Get("BalloonFightingTitle"),
                    L.Get("BalloonFightingText"));
                return;
            }

            try
            {
                DisplayService.SetRotation(mon, want);
            }
            catch (Exception ex)
            {
                _showWarning(L.Get("BalloonReapplyFailedTitle"), ex.Message);
            }
        }

        if (!anyMismatch)
        {
            // Everything is correct: reset thrash state and un-pause.
            _correctiveCount     = 0;
            _firstCorrectiveTime = DateTime.MinValue;
            _thrashPaused        = false;
        }
    }

    public void Dispose() => _debounce.Stop();
}
