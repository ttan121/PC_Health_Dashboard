using System;

namespace PCHealthDashboard.Models;

/// <summary>
/// Progress information dispatched during batch disk cleaning operations.
/// </summary>
public readonly record struct DiskCleaningProgress(
    string CurrentFile,
    long CleanedBytes,
    int FilesDeleted,
    int FilesSkippedLocked,
    int FilesSkippedAccessDenied
)
{
    public double CleanedMB => CleanedBytes / 1024.0 / 1024.0;
}

/// <summary>
/// Comprehensive summary report returned after completing disk cleaning operations.
/// </summary>
public readonly record struct DiskCleaningReport(
    long InitialJunkBytes,
    long TotalCleanedBytes,
    int FilesDeleted,
    int FilesSkippedLocked,
    int FilesSkippedAccessDenied,
    int DirectoriesRemoved,
    bool RecycleBinEmptied,
    TimeSpan Duration,
    string Details
)
{
    public double InitialJunkMB => InitialJunkBytes / 1024.0 / 1024.0;
    public double TotalCleanedMB => TotalCleanedBytes / 1024.0 / 1024.0;
}
