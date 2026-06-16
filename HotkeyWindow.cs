using System.Windows.Forms;
using Windows.Win32.Foundation;

namespace RotatePlus;

/// <summary>
/// A hidden native window whose sole job is to receive WM_HOTKEY and
/// WM_DISPLAYCHANGE messages and forward them via callbacks.
/// </summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WM_HOTKEY       = 0x0312;
    private const int WM_DISPLAYCHANGE = 0x007E;

    private readonly Action<int> _onHotkey;
    private readonly Action      _onDisplayChange;

    public HWND HWND => (HWND)Handle;

    public HotkeyWindow(Action<int> onHotkey, Action onDisplayChange)
    {
        _onHotkey        = onHotkey;
        _onDisplayChange = onDisplayChange;

        // Hidden top-level window (never shown). Deliberately NOT a message-only
        // (HWND_MESSAGE) window: those don't reliably receive WM_HOTKEY, which is a
        // queued thread message dispatched to a normal window via the message loop.
        CreateHandle(new CreateParams
        {
            Caption = "rotate+ hotkey sink",
            Style   = 0,           // WS_OVERLAPPED, no WS_VISIBLE → never shown
            ExStyle = 0x00000080,  // WS_EX_TOOLWINDOW → no taskbar entry
        });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            _onHotkey((int)m.WParam);
            return;
        }
        if (m.Msg == WM_DISPLAYCHANGE)
        {
            _onDisplayChange();
            // Fall through to base so the system can finish its own handling.
        }
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
