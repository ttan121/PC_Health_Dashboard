using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCHealthDashboard.Helpers;
using PCHealthDashboard.Models;
using PCHealthDashboard.Services;
using PCHealthDashboard.ViewModels;
using Xunit;

namespace PCHealthDashboard.Tests;

public class ByteSizeFormatterAndViewModelTests
{
    [Theory]
    [InlineData(0, "0 MB")]
    [InlineData(-5, "0 MB")]
    [InlineData(0.5, "0.50 MB")]
    [InlineData(500, "500 MB")]
    [InlineData(1023, "1023 MB")]
    [InlineData(1023.4, "1023 MB")]
    [InlineData(1023.9, "1024 MB (1.00 GB)")]
    [InlineData(1024, "1024 MB (1.00 GB)")]
    [InlineData(1536, "1536 MB (1.50 GB)")]
    [InlineData(2048, "2048 MB (2.00 GB)")]
    [InlineData(1048576, "1048576 MB (1024.00 GB)")] // 1 TB in MB
    [InlineData(5242880, "5242880 MB (5120.00 GB)")] // 5 TB in MB
    public void ByteSizeFormatter_FormatMb_ShouldFormatCorrectly(double mb, string expected)
    {
        string result = ByteSizeFormatter.FormatMb(mb);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ByteSizeFormatter_FormatMb_SpecialValues()
    {
        Assert.Equal("0 MB", ByteSizeFormatter.FormatMb(double.NaN));
        Assert.Equal("0 MB", ByteSizeFormatter.FormatMb(double.PositiveInfinity));
        Assert.Equal("0 MB", ByteSizeFormatter.FormatMb(double.NegativeInfinity));
    }

    [Fact]
    public void ByteSizeFormatter_FormatBytes_ShouldConvertAndFormat()
    {
        long bytes500Mb = 500L * 1024 * 1024;
        Assert.Equal("500 MB", ByteSizeFormatter.FormatBytes(bytes500Mb));

        long bytes1Gb = 1024L * 1024 * 1024;
        Assert.Equal("1024 MB (1.00 GB)", ByteSizeFormatter.FormatBytes(bytes1Gb));

        long bytes2Gb = 2048L * 1024 * 1024;
        Assert.Equal("2048 MB (2.00 GB)", ByteSizeFormatter.FormatBytes(bytes2Gb));

        // 2 Terabytes in bytes
        long bytes2Tb = 2L * 1024 * 1024 * 1024 * 1024;
        Assert.Equal("2097152 MB (2048.00 GB)", ByteSizeFormatter.FormatBytes(bytes2Tb));

        // Negative / 0
        Assert.Equal("0 MB", ByteSizeFormatter.FormatBytes(0));
        Assert.Equal("0 MB", ByteSizeFormatter.FormatBytes(-100));
    }

    private class MockMemoryService : INativeMemoryService
    {
        public List<ProcessItem> ProcessesToReturn = new();

        public Task<List<ProcessItem>> GetEligibleProcessesAsync() => Task.FromResult(ProcessesToReturn);
        public Task<OptimizationResult> OptimizeProcessAsync(int pid) => Task.FromResult(new OptimizationResult());
        public RamOptimizationReport OptimizeRamDeep() => new RamOptimizationReport();
    }

    [Fact]
    public void RamOptimizerViewModel_SelectionUpdatesTotalSelected()
    {
        var mockService = new MockMemoryService();
        var vm = new RamOptimizerViewModel(mockService);

        var p1 = new ProcessItem { Pid = 1, ProcessName = "App1", WorkingSet64 = 500L * 1024 * 1024, IsSelected = false };
        var p2 = new ProcessItem { Pid = 2, ProcessName = "App2", WorkingSet64 = 600L * 1024 * 1024, IsSelected = false };

        vm.Processes.Add(p1);
        vm.Processes.Add(p2);
        p1.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ProcessItem.IsSelected)) vm.UpdateTotalSelected(); };
        p2.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ProcessItem.IsSelected)) vm.UpdateTotalSelected(); };

        vm.UpdateTotalSelected();
        Assert.Equal("0 MB", vm.TotalSelectedSize);
        Assert.False(vm.IsAllSelected);

        // Select p1 (500 MB)
        p1.IsSelected = true;
        Assert.Equal("500 MB", vm.TotalSelectedSize);
        Assert.False(vm.IsAllSelected);

        // Select p2 (500 + 600 = 1100 MB >= 1024 MB -> GB format)
        p2.IsSelected = true;
        Assert.Contains("(1.07 GB)", vm.TotalSelectedSize);
        Assert.StartsWith("1100 MB", vm.TotalSelectedSize);
        Assert.True(vm.IsAllSelected);

        // Deselect all
        p1.IsSelected = false;
        p2.IsSelected = false;
        Assert.Equal("0 MB", vm.TotalSelectedSize);
        Assert.False(vm.IsAllSelected);

        // IsAllSelected = true selects both
        vm.IsAllSelected = true;
        Assert.True(p1.IsSelected);
        Assert.True(p2.IsSelected);
        Assert.Contains("(1.07 GB)", vm.TotalSelectedSize);
    }

    private class MockDiskCleanerService : IDiskCleanerService
    {
        public DiskScanResult ResultToReturn = new();
        public Task<DiskScanResult> ScanJunkAsync(IEnumerable<string>? driveRoots, CancellationToken ct) => Task.FromResult(ResultToReturn);
        public Task<DiskCleaningReport> CleanJunkAsync(IProgress<DiskCleaningProgress>? progress, CancellationToken ct) => Task.FromResult(new DiskCleaningReport());
        public Task<DiskCleaningReport> CleanSpecificFilesAsync(IEnumerable<string> filePaths, bool emptyRecycleBin, IEnumerable<string>? drivesForRecycleBin, IProgress<DiskCleaningProgress>? progress, CancellationToken ct) => Task.FromResult(new DiskCleaningReport());
    }

    [Fact]
    public void JunkCleanerViewModel_InitialStateAndSelection()
    {
        var mockService = new MockDiskCleanerService();
        var vm = new JunkCleanerViewModel(mockService);

        // Initial state: CanClean should be false, CleanCommand cannot execute, and Total should be 0 MB
        Assert.False(vm.CanClean);
        Assert.False(vm.CleanCommand.CanExecute(null));
        Assert.True(vm.CanScan);
        Assert.True(vm.ScanCommand.CanExecute(null));
        Assert.Equal("0 MB", vm.TotalJunkSize);

        // Add 500 MB item
        var item1 = new JunkItem { FilePath = "C:\\temp\\file1.tmp", Size = 500L * 1024 * 1024, IsSelected = true };
        item1.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(JunkItem.IsSelected)) vm.UpdateTotalJunkSize(); };
        vm.JunkFiles.Add(item1);
        vm.UpdateTotalJunkSize();

        Assert.True(vm.CanClean);
        Assert.True(vm.CleanCommand.CanExecute(null));
        Assert.Equal("500 MB", vm.TotalJunkSize);

        // Add 600 MB item (Total = 1100 MB -> GB format)
        var item2 = new JunkItem { FilePath = "C:\\temp\\file2.tmp", Size = 600L * 1024 * 1024, IsSelected = true };
        item2.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(JunkItem.IsSelected)) vm.UpdateTotalJunkSize(); };
        vm.JunkFiles.Add(item2);
        vm.UpdateTotalJunkSize();

        Assert.True(vm.CanClean);
        Assert.True(vm.CleanCommand.CanExecute(null));
        Assert.Contains("(1.07 GB)", vm.TotalJunkSize);

        // Deselect all items -> CanClean must be false, CleanCommand cannot execute, Total must be 0 MB
        item1.IsSelected = false;
        item2.IsSelected = false;
        Assert.False(vm.CanClean);
        Assert.False(vm.CleanCommand.CanExecute(null));
        Assert.Equal("0 MB", vm.TotalJunkSize);
    }

    [Theory]
    [InlineData("Yellow", 253, 224, 71)]
    [InlineData("yellow", 253, 224, 71)]
    [InlineData("#fde047", 253, 224, 71)]
    [InlineData("Orange", 251, 146, 60)]
    [InlineData("orange", 251, 146, 60)]
    [InlineData("#fb923c", 251, 146, 60)]
    [InlineData("#f59e0b", 251, 146, 60)]
    [InlineData("Red", 248, 113, 113)]
    [InlineData("red", 248, 113, 113)]
    [InlineData("#f87171", 248, 113, 113)]
    [InlineData("Green", 74, 222, 128)]
    [InlineData("green", 74, 222, 128)]
    [InlineData("#4ade80", 74, 222, 128)]
    public void StringToColorBrushConverter_ShouldConvertCorrectColors(string input, byte r, byte g, byte b)
    {
        var converter = new StringToColorBrushConverter();
        var result = converter.Convert(input, typeof(System.Windows.Media.SolidColorBrush), null!, System.Globalization.CultureInfo.InvariantCulture);
        var brush = Assert.IsType<System.Windows.Media.SolidColorBrush>(result);
        Assert.Equal(r, brush.Color.R);
        Assert.Equal(g, brush.Color.G);
        Assert.Equal(b, brush.Color.B);
    }

    [Fact]
    public void StringToBoolConverter_ShouldMatchParameter()
    {
        var converter = new StringToBoolConverter();
        Assert.True((bool)converter.Convert("Orange", typeof(bool), "Orange", System.Globalization.CultureInfo.InvariantCulture));
        Assert.False((bool)converter.Convert("Orange", typeof(bool), "Yellow", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Orange", converter.ConvertBack(true, typeof(string), "Orange", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void MainViewModel_DefaultOsdColorIsOrange()
    {
        var vm = new MainViewModel();
        Assert.Equal("Orange", vm.OsdColor);
    }
}
