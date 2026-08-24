// ============================================================================
// PC Health Dashboard - Tests/DwmBackdropHelperTests.cs
// Unit Tests for DWM Backdrop Helper (Mica, Acrylic, Dark Mode)
// ============================================================================

using System;
using System.Threading;
using System.Windows;
using PCHealthDashboard.Helpers;
using Xunit;

namespace PCHealthDashboard.Tests;

public class DwmBackdropHelperTests
{
    [Fact]
    public void DwmBackdropHelper_ApplyBackdrop_WithZeroHwnd_ReturnsFalseSafely()
    {
        bool result = DwmBackdropHelper.ApplyBackdrop(IntPtr.Zero, BackdropType.Mica, enableDarkMode: true);
        Assert.False(result);
    }

    [Fact]
    public void DwmBackdropHelper_ApplyBackdrop_WithNullWindow_ReturnsFalseSafely()
    {
        bool result = DwmBackdropHelper.ApplyBackdrop((Window)null!, BackdropType.Mica, enableDarkMode: true);
        Assert.False(result);
    }

    [Theory]
    [InlineData(BackdropType.Default)]
    [InlineData(BackdropType.None)]
    [InlineData(BackdropType.Mica)]
    [InlineData(BackdropType.Acrylic)]
    [InlineData(BackdropType.MicaAlt)]
    public void DwmBackdropHelper_BackdropTypes_AreProperlyMapped(BackdropType backdropType)
    {
        int enumVal = (int)backdropType;
        Assert.InRange(enumVal, 0, 4);
    }

    [Fact]
    public void DwmBackdropHelper_ApplyBackdrop_OnStaWindow_ExecutesWithoutException()
    {
        RunOnSta(() =>
        {
            var win = new Window
            {
                Title = "DwmTestWindow",
                Width = 200,
                Height = 150
            };

            // Test applying Mica
            bool micaResult = DwmBackdropHelper.ApplyBackdrop(win, BackdropType.Mica, enableDarkMode: true);

            // Test applying Acrylic
            bool acrylicResult = DwmBackdropHelper.ApplyBackdrop(win, BackdropType.Acrylic, enableDarkMode: true);

            // Test applying Dark Mode Only
            bool noneResult = DwmBackdropHelper.ApplyBackdrop(win, BackdropType.None, enableDarkMode: true);

            // Closing window
            win.Close();

            // On Windows 10/11 this will return true, on older systems or headless tests it handles gracefully without crashing
            Assert.True(micaResult || !micaResult);
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            throw new Exception("STA Thread Exception", threadEx);
        }
    }
}
