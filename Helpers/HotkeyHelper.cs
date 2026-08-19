using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PCHealthDashboard.Helpers;

public class HotkeyHelper : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;

    private readonly IntPtr _hwnd;
    private readonly int _idPopup = 9000;
    private readonly int _idCompact = 9001;
    
    private Action _onPopupHotKeyPressed;
    private Action _onCompactHotKeyPressed;

    public HotkeyHelper(IntPtr hwnd, Action onPopupHotKeyPressed, Action onCompactHotKeyPressed)
    {
        _hwnd = hwnd;
        _onPopupHotKeyPressed = onPopupHotKeyPressed;
        _onCompactHotKeyPressed = onCompactHotKeyPressed;
        
        HwndSource source = HwndSource.FromHwnd(_hwnd);
        source.AddHook(HwndHook);

        uint keySpace = 0x20; // Space
        
        // Register Ctrl + Shift + Space for Popup
        RegisterHotKey(_hwnd, _idPopup, MOD_CONTROL | MOD_SHIFT, keySpace);
        
        // Register Ctrl + Shift + Alt + Space for Compact Mode
        RegisterHotKey(_hwnd, _idCompact, MOD_CONTROL | MOD_SHIFT | MOD_ALT, keySpace);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == _idPopup)
            {
                _onPopupHotKeyPressed?.Invoke();
                handled = true;
            }
            else if (id == _idCompact)
            {
                _onCompactHotKeyPressed?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotKey(_hwnd, _idPopup);
        UnregisterHotKey(_hwnd, _idCompact);
    }
}
