using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PCHealthDashboard.Models;
using PCHealthDashboard.Services;
using Xunit;

namespace PCHealthDashboard.Tests;

public class DiskCleanerTests
{
    [Fact]
    public void SafeDeleteFile_NormalFile_ShouldDeleteSuccessfully()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"pchealth_test_normal_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempFile, "Temporary test content");

        try
        {
            var status = DiskCleanerService.SafeDeleteFile(tempFile);
            Assert.Equal(DeletionStatus.Deleted, status);
            Assert.False(File.Exists(tempFile), "File should no longer exist.");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SafeDeleteFile_ReadOnlyFile_ShouldClearAttributesAndSucceed()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"pchealth_test_readonly_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempFile, "Read only test content");
        File.SetAttributes(tempFile, FileAttributes.ReadOnly);

        try
        {
            var status = DiskCleanerService.SafeDeleteFile(tempFile);
            Assert.Equal(DeletionStatus.Deleted, status);
            Assert.False(File.Exists(tempFile), "Read-only file should be deleted after clearing attribute.");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.SetAttributes(tempFile, FileAttributes.Normal);
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void SafeDeleteFile_LockedFile_ShouldGracefullyReturnLockedWithoutThrowing()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"pchealth_test_locked_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempFile, "Locked test content");

        using (var fs = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var status = DiskCleanerService.SafeDeleteFile(tempFile);
            Assert.Equal(DeletionStatus.Locked, status);
            Assert.True(File.Exists(tempFile), "Locked file should still exist.");
        }

        // Cleanup after stream closed
        File.Delete(tempFile);
    }

    [Fact]
    public async Task CleanSpecificFilesAsync_ShouldHandleMixedLockedAndNormalFiles()
    {
        string file1 = Path.Combine(Path.GetTempPath(), $"pchealth_batch_1_{Guid.NewGuid():N}.tmp");
        string file2 = Path.Combine(Path.GetTempPath(), $"pchealth_batch_2_locked_{Guid.NewGuid():N}.tmp");
        string file3 = Path.Combine(Path.GetTempPath(), $"pchealth_batch_3_ro_{Guid.NewGuid():N}.tmp");

        File.WriteAllText(file1, "Normal content");
        File.WriteAllText(file2, "Locked content");
        File.WriteAllText(file3, "Read-only content");
        File.SetAttributes(file3, FileAttributes.ReadOnly);

        var service = new DiskCleanerService();
        var progressReports = new System.Collections.Generic.List<DiskCleaningProgress>();
        var progress = new Progress<DiskCleaningProgress>(p => progressReports.Add(p));

        using (var lockStream = new FileStream(file2, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var report = await service.CleanSpecificFilesAsync(
                new[] { file1, file2, file3 },
                emptyRecycleBin: false,
                drivesForRecycleBin: null,
                progress: progress,
                ct: CancellationToken.None
            );

            Assert.Equal(2, report.FilesDeleted); // file1 and file3
            Assert.Equal(1, report.FilesSkippedLocked); // file2
            Assert.False(File.Exists(file1));
            Assert.True(File.Exists(file2));
            Assert.False(File.Exists(file3));
        }

        if (File.Exists(file2)) File.Delete(file2);
    }
}
