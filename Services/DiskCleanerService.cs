using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PCHealthDashboard.Helpers;
using PCHealthDashboard.Models;

namespace PCHealthDashboard.Services;

public enum DeletionStatus
{
    Deleted,
    Locked,
    AccessDenied,
    AlreadyDeleted,
    Failed
}

public readonly record struct ScannedJunkFile(string FilePath, long SizeBytes, string Category);

public readonly record struct DiskScanResult(
    IReadOnlyList<ScannedJunkFile> Files,
    long TotalSizeBytes,
    int TotalFilesCount
);

public interface IDiskCleanerService
{
    Task<DiskScanResult> ScanJunkAsync(IEnumerable<string>? driveRoots, CancellationToken ct);
    Task<DiskCleaningReport> CleanJunkAsync(IProgress<DiskCleaningProgress>? progress, CancellationToken ct);
    Task<DiskCleaningReport> CleanSpecificFilesAsync(
        IEnumerable<string> filePaths,
        bool emptyRecycleBin,
        IEnumerable<string>? drivesForRecycleBin,
        IProgress<DiskCleaningProgress>? progress,
        CancellationToken ct
    );
}

/// <summary>
/// Robust, low-overhead disk cleanup service with Win32 SHEmptyRecycleBin integration,
/// safe attribute resetting, locked file exception isolation, and non-blocking batch reporting.
/// </summary>
public class DiskCleanerService : IDiskCleanerService
{
    public async Task<DiskScanResult> ScanJunkAsync(IEnumerable<string>? driveRoots, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var results = new List<ScannedJunkFile>();
            var targetDirs = GetTargetDirectories(driveRoots);

            foreach (var (dirPath, category) in targetDirs)
            {
                if (ct.IsCancellationRequested) break;
                ScanDirectoryRecursive(dirPath, category, results, ct);
            }

            long totalBytes = results.Sum(x => x.SizeBytes);
            return new DiskScanResult(results, totalBytes, results.Count);
        }, ct);
    }

    public async Task<DiskCleaningReport> CleanJunkAsync(IProgress<DiskCleaningProgress>? progress, CancellationToken ct)
    {
        var scanResult = await ScanJunkAsync(null, ct);
        var filesToDelete = scanResult.Files.Select(f => f.FilePath).ToList();
        return await CleanSpecificFilesAsync(filesToDelete, true, null, progress, ct);
    }

    public async Task<DiskCleaningReport> CleanSpecificFilesAsync(
        IEnumerable<string> filePaths,
        bool emptyRecycleBin,
        IEnumerable<string>? drivesForRecycleBin,
        IProgress<DiskCleaningProgress>? progress,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            long initialBytes = 0;
            long cleanedBytes = 0;
            int deletedCount = 0;
            int lockedCount = 0;
            int accessDeniedCount = 0;
            int dirsRemoved = 0;
            bool recycleBinSuccess = false;

            var touchedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lastReportTime = Stopwatch.GetTimestamp();

            foreach (var path in filePaths)
            {
                if (ct.IsCancellationRequested) break;

                long fileSize = 0;
                try
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists)
                    {
                        fileSize = fi.Length;
                        initialBytes += fileSize;
                        string? dir = fi.DirectoryName;
                        if (!string.IsNullOrEmpty(dir)) touchedDirs.Add(dir);
                    }
                }
                catch
                {
                    // Skip metadata read error
                }

                var status = SafeDeleteFile(path);
                switch (status)
                {
                    case DeletionStatus.Deleted:
                        deletedCount++;
                        cleanedBytes += fileSize;
                        break;
                    case DeletionStatus.Locked:
                        lockedCount++;
                        break;
                    case DeletionStatus.AccessDenied:
                        accessDeniedCount++;
                        break;
                    case DeletionStatus.AlreadyDeleted:
                        break;
                    default:
                        lockedCount++;
                        break;
                }

                // Throttled non-blocking progress dispatch (~every 40ms or 25 items)
                if (progress != null && ((deletedCount + lockedCount + accessDeniedCount) % 25 == 0 ||
                    Stopwatch.GetElapsedTime(lastReportTime).TotalMilliseconds > 40))
                {
                    lastReportTime = Stopwatch.GetTimestamp();
                    progress.Report(new DiskCleaningProgress(
                        CurrentFile: path,
                        CleanedBytes: cleanedBytes,
                        FilesDeleted: deletedCount,
                        FilesSkippedLocked: lockedCount,
                        FilesSkippedAccessDenied: accessDeniedCount
                    ));
                }
            }

            // Cleanup empty child directories
            foreach (var dir in touchedDirs)
            {
                if (ct.IsCancellationRequested) break;
                dirsRemoved += TryCleanEmptyDirectoryTree(dir);
            }

            // Empty Recycle Bin via Win32 Shell API
            if (emptyRecycleBin && !ct.IsCancellationRequested)
            {
                recycleBinSuccess = EmptyRecycleBins(drivesForRecycleBin);
            }

            sw.Stop();

            // Final progress report
            progress?.Report(new DiskCleaningProgress(
                CurrentFile: "Completed",
                CleanedBytes: cleanedBytes,
                FilesDeleted: deletedCount,
                FilesSkippedLocked: lockedCount,
                FilesSkippedAccessDenied: accessDeniedCount
            ));

            string details = $"Deleted {deletedCount} files ({cleanedBytes / 1024.0 / 1024.0:F2} MB). " +
                             $"Skipped {lockedCount} locked files, {accessDeniedCount} access denied. " +
                             $"Cleaned {dirsRemoved} empty folders. " +
                             $"Recycle Bin: {(recycleBinSuccess ? "Emptied" : "Unchanged/Skipped")}.";

            return new DiskCleaningReport(
                InitialJunkBytes: initialBytes,
                TotalCleanedBytes: cleanedBytes,
                FilesDeleted: deletedCount,
                FilesSkippedLocked: lockedCount,
                FilesSkippedAccessDenied: accessDeniedCount,
                DirectoriesRemoved: dirsRemoved,
                RecycleBinEmptied: recycleBinSuccess,
                Duration: sw.Elapsed,
                Details: details
            );
        }, ct);
    }

    public static DeletionStatus SafeDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return DeletionStatus.AlreadyDeleted;

            // Clear ReadOnly / Hidden / System flags so File.Delete succeeds
            var attr = File.GetAttributes(path);
            if ((attr & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            File.Delete(path);
            return DeletionStatus.Deleted;
        }
        catch (IOException)
        {
            // Sharing violation or file in active use
            return DeletionStatus.Locked;
        }
        catch (UnauthorizedAccessException)
        {
            // Security ACL / Permission denied
            return DeletionStatus.AccessDenied;
        }
        catch (Exception)
        {
            return DeletionStatus.Failed;
        }
    }

    private static int TryCleanEmptyDirectoryTree(string dirPath)
    {
        int count = 0;
        try
        {
            if (!Directory.Exists(dirPath)) return 0;

            // Recursively try to remove empty subdirectories first
            foreach (var sub in Directory.GetDirectories(dirPath))
            {
                count += TryCleanEmptyDirectoryTree(sub);
            }

            // Do not delete root special folders themselves, only empty custom subdirectories
            if (!IsRootSpecialFolder(dirPath) && !Directory.EnumerateFileSystemEntries(dirPath).Any())
            {
                Directory.Delete(dirPath, false);
                count++;
            }
        }
        catch
        {
            // Ignore lock or permission errors on directory deletion
        }
        return count;
    }

    private static bool IsRootSpecialFolder(string path)
    {
        string norm = path.TrimEnd('\\', '/');
        var protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetTempPath().TrimEnd('\\', '/'),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        return protectedPaths.Contains(norm);
    }

    private static bool EmptyRecycleBins(IEnumerable<string>? driveRoots)
    {
        bool anySuccess = false;
        const uint flags = NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND;

        try
        {
            if (driveRoots == null || !driveRoots.Any())
            {
                // Empty all drives at once
                int hr = NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, flags);
                return hr == 0;
            }

            foreach (var drive in driveRoots)
            {
                try
                {
                    int hr = NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, drive, flags);
                    if (hr == 0) anySuccess = true;
                }
                catch
                {
                    // Continue to next drive
                }
            }
        }
        catch
        {
            // Graceful fallback
        }

        return anySuccess;
    }

    private static void ScanDirectoryRecursive(
        string dirPath,
        string category,
        List<ScannedJunkFile> results,
        CancellationToken ct)
    {
        try
        {
            var dir = new DirectoryInfo(dirPath);
            if (!dir.Exists) return;

            // Never scan raw $Recycle.Bin with DirectoryInfo to avoid SID lock issues
            if (dir.Name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)) return;

            FileInfo[] files;
            try
            {
                files = dir.GetFiles();
            }
            catch
            {
                return;
            }

            foreach (var f in files)
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    results.Add(new ScannedJunkFile(f.FullName, f.Length, category));
                }
                catch
                {
                    // Skip
                }
            }

            DirectoryInfo[] subDirs;
            try
            {
                subDirs = dir.GetDirectories();
            }
            catch
            {
                return;
            }

            foreach (var sub in subDirs)
            {
                if (ct.IsCancellationRequested) return;
                ScanDirectoryRecursive(sub.FullName, category, results, ct);
            }
        }
        catch
        {
            // Skip directory access errors
        }
    }

    private static List<(string Path, string Category)> GetTargetDirectories(IEnumerable<string>? driveRoots)
    {
        var list = new List<(string Path, string Category)>();

        // 1. User Temp
        string userTemp = Path.GetTempPath();
        if (Directory.Exists(userTemp)) list.Add((userTemp, "User Temp"));

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string localTemp = Path.Combine(localAppData, "Temp");
        if (Directory.Exists(localTemp) && !localTemp.Equals(userTemp, StringComparison.OrdinalIgnoreCase))
        {
            list.Add((localTemp, "User Temp"));
        }

        // 2. Windows System Temp
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string sysTemp = Path.Combine(winDir, "Temp");
        if (Directory.Exists(sysTemp)) list.Add((sysTemp, "System Temp"));

        // 3. Windows Update Download Cache
        string softDist = Path.Combine(winDir, "SoftwareDistribution", "Download");
        if (Directory.Exists(softDist)) list.Add((softDist, "Windows Update Cache"));

        // 4. Crash Dumps
        string crashDumps = Path.Combine(localAppData, "CrashDumps");
        if (Directory.Exists(crashDumps)) list.Add((crashDumps, "Crash Dumps"));

        // 5. Windows Error Reporting (WER)
        string werArchive = Path.Combine(localAppData, "Microsoft", "Windows", "WER", "ReportArchive");
        if (Directory.Exists(werArchive)) list.Add((werArchive, "Windows Error Reports"));

        string werQueue = Path.Combine(localAppData, "Microsoft", "Windows", "WER", "ReportQueue");
        if (Directory.Exists(werQueue)) list.Add((werQueue, "Windows Error Reports"));

        string commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string werCommon = Path.Combine(commonAppData, "Microsoft", "Windows", "WER", "ReportArchive");
        if (Directory.Exists(werCommon)) list.Add((werCommon, "Windows Error Reports"));

        // 6. Browser Caches
        // Edge Cache
        string edgeCache = Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache\Cache_Data");
        if (Directory.Exists(edgeCache)) list.Add((edgeCache, "Edge Cache"));

        // Chrome Cache
        string chromeCache = Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache\Cache_Data");
        if (Directory.Exists(chromeCache)) list.Add((chromeCache, "Chrome Cache"));

        // Brave Cache
        string braveCache = Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Cache\Cache_Data");
        if (Directory.Exists(braveCache)) list.Add((braveCache, "Brave Cache"));

        // Firefox Cache
        string firefoxProfiles = Path.Combine(localAppData, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(firefoxProfiles))
        {
            try
            {
                foreach (var profile in Directory.GetDirectories(firefoxProfiles))
                {
                    string cache2 = Path.Combine(profile, "cache2", "entries");
                    if (Directory.Exists(cache2)) list.Add((cache2, "Firefox Cache"));
                }
            }
            catch { }
        }

        // 7. Drive Temp Folders
        var drives = driveRoots ?? DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Select(d => d.Name);

        foreach (var drive in drives)
        {
            try
            {
                string rootTemp = Path.Combine(drive, "Temp");
                if (Directory.Exists(rootTemp) && !rootTemp.Equals(sysTemp, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add((rootTemp, "Drive Temp"));
                }
            }
            catch { }
        }

        return list;
    }
}
