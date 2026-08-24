// ============================================================================
// PC Health Dashboard - Helpers/DwmBackdropHelper.cs
// Modern Windows DWM Backdrop P/Invoke (Mica, Acrylic, Dark Mode, Round Corners)
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PCHealthDashboard.Helpers;

/// <summary>
/// DWM System Backdrop types supported on Windows 11 22H2 (Build 22621+).
/// </summary>
public enum BackdropType
{
    Default = 0,
    None = 1,
    Mica = 2,
    Acrylic = 3,
    MicaAlt = 4
}

/// <summary>
/// Helper class for applying Windows Desktop Window Manager (DWM) backdrop materials,
/// dark mode titlebars, and window frame attributes via low-level native Win32 APIs.
/// </summary>
public static class DwmBackdropHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_MICA_EFFECT = 1029;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMWCP_ROUND = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    /// <summary>
    /// Applies DWM backdrop effect (Mica / Acrylic) and dark mode attributes to a WPF Window.
    /// </summary>
    /// <param name="window">WPF window instance.</param>
    /// <param name="backdrop">Target backdrop type (Mica, Acrylic, MicaAlt).</param>
    /// <param name="enableDarkMode">Whether to enable immersive dark mode frame.</param>
    /// <returns>True if applied successfully; false otherwise.</returns>
    public static bool ApplyBackdrop(Window window, BackdropType backdrop = BackdropType.Mica, bool enableDarkMode = true)
    {
        if (window == null) return false;
        var helper = new WindowInteropHelper(window);
        IntPtr hwnd = helper.EnsureHandle();
        return ApplyBackdrop(hwnd, backdrop, enableDarkMode);
    }

    /// <summary>
    /// Applies DWM backdrop effect and dark mode attributes to a native window handle (HWND).
    /// </summary>
    /// <param name="hwnd">Native Win32 window handle.</param>
    /// <param name="backdrop">Target backdrop type.</param>
    /// <param name="enableDarkMode">Whether to enable immersive dark mode.</param>
    /// <returns>True if applied successfully; false otherwise.</returns>
    public static bool ApplyBackdrop(IntPtr hwnd, BackdropType backdrop = BackdropType.Mica, bool enableDarkMode = true)
    {
        if (hwnd == IntPtr.Zero) return false;

        try
        {
            int build = Environment.OSVersion.Version.Build;

            // 1. Enable Immersive Dark Mode
            if (enableDarkMode)
            {
                int darkMode = 1;
                if (build >= 18985)
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                }
                else if (build >= 17763)
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));
                }
            }

            // 2. Round Corners on Windows 11 (Build >= 22000)
            if (build >= 22000)
            {
                int round = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            }

            // 3. Extend Frame into Client Area
            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            // 4. Apply System Backdrop (Win11 22H2+ Build >= 22621) or Legacy Mica (Win11 21H2 Build 22000)
            if (build >= 22621)
            {
                int type = (int)backdrop;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
            }
            else if (build >= 22000 && backdrop == BackdropType.Mica)
            {
                int mica = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_MICA_EFFECT, ref mica, sizeof(int));
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
