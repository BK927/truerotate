using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TrueRotate;

/// <summary>
/// A hidden top-level Win32 window whose sole job is to receive WM_HOTKEY and
/// WM_DISPLAYCHANGE messages and forward them to callbacks.
///
/// Deliberately NOT a message-only (HWND_MESSAGE) window: message-only windows
/// don't reliably receive WM_HOTKEY, which is a queued thread message dispatched
/// to a normal window via the message loop.
/// </summary>
internal sealed class HotkeyWindow : IDisposable
{
    private const uint WM_HOTKEY        = 0x0312;
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private const uint WM_DESTROY       = 0x0002;

    private readonly Action<int> _onHotkey;
    private readonly Action      _onDisplayChange;

    // Keep the delegate alive for the lifetime of the window — prevents GC
    private readonly WNDPROC _wndProcDelegate;

    private HWND _hwnd;

    public HWND HWND => _hwnd;

    public HotkeyWindow(Action<int> onHotkey, Action onDisplayChange)
    {
        _onHotkey        = onHotkey;
        _onDisplayChange = onDisplayChange;
        _wndProcDelegate = WndProc;

        CreateHiddenWindow();
    }

    private unsafe void CreateHiddenWindow()
    {
        const string ClassName = "TrueRotate_HotkeyWindow";

        HINSTANCE hInst = PInvoke.GetModuleHandle((PCWSTR)null);

        fixed (char* pClassName = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize        = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc   = _wndProcDelegate,
                hInstance     = hInst,
                lpszClassName = pClassName,
            };

            // It's fine if RegisterClassEx fails (class already registered from
            // a prior run in the same process — shouldn't happen, but be safe).
            PInvoke.RegisterClassEx(wc);
        }

        fixed (char* pClassName = ClassName)
        fixed (char* pTitle     = "TrueRotate hotkey sink")
        {
            _hwnd = PInvoke.CreateWindowEx(
                dwExStyle:     WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,  // no taskbar entry
                lpClassName:   pClassName,
                lpWindowName:  pTitle,
                dwStyle:       WINDOW_STYLE.WS_OVERLAPPED,         // no WS_VISIBLE → never shown
                X: 0, Y: 0, nWidth: 0, nHeight: 0,
                hWndParent:    default,
                hMenu:         default,
                hInstance:     hInst,
                lpParam:       null);
        }

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException(
                $"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
    }

    private LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (msg == WM_HOTKEY)
        {
            _onHotkey((int)(nuint)wParam);
            return new LRESULT(0);
        }

        if (msg == WM_DISPLAYCHANGE)
        {
            _onDisplayChange();
            // Fall through to DefWindowProc so the system finishes its own handling.
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            PInvoke.DestroyWindow(_hwnd);
            _hwnd = default;
        }
    }
}
