using System.Windows.Input;
using System.Windows.Interop;
using static GlideBar.App.Interop.NativeMethods;

namespace GlideBar.App.Interop;

/// <summary>
/// Registers a single global hotkey (default Ctrl+Space) via <c>RegisterHotKey</c> and raises
/// <see cref="Pressed"/> when it fires. Hooks the window's message loop; disposable.
/// </summary>
internal sealed class HotKeyService : IDisposable
{
    private const int HotKeyId = 0xB001;
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private bool _registered;

    public event EventHandler? Pressed;

    public HotKeyService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("No HwndSource for hotkey.");
        _source.AddHook(WndProc);
    }

    /// <summary>Registers (or re-registers) from a string like "Ctrl+Space". Returns success.</summary>
    public bool Register(string hotkey)
    {
        Unregister();
        if (!TryParse(hotkey, out uint mods, out uint vk)) return false;
        _registered = RegisterHotKey(_hwnd, HotKeyId, mods | MOD_NOREPEAT, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_hwnd, HotKeyId);
            _registered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotKeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>Parses "Ctrl+Shift+Space" into Win32 modifier flags + virtual key.</summary>
    public static bool TryParse(string? hotkey, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(hotkey)) return false;

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Key key = Key.None;
        foreach (var raw in parts)
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win": case "windows": case "meta": mods |= MOD_WIN; break;
                case "space": key = Key.Space; break;
                default:
                    if (!Enum.TryParse(raw, true, out key)) return false;
                    break;
            }
        }
        if (key == Key.None) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
    }
}
