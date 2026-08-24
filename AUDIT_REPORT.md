# RAM & Disk Cleaner Architecture & Safety Audit Report

**Project**: PC Health Dashboard  
**Milestone**: M4 (RAM & Disk Cleaner Audit & Optimize)  
**Author**: Worker 2 (Systems Cleaner Engineer)  
**Date**: 2026-08-24  
**Status**: VERIFIED & PRODUCTION READY  

---

## 1. Executive Summary

As part of Requirement **R4 (RAM & Disk Cleaner Audit & Optimize)**, a comprehensive deep audit was conducted on both the legacy memory optimization and disk junk cleaning subsystems of PC Health Dashboard. 

The audit identified critical architectural gaps in both modules:
1. **RAM Optimization**: Legacy implementations relied solely on `EmptyWorkingSet(hProcess)` against individual user processes. This merely paged private working sets into the Windows Standby Cache, failing to release memory to the system's Free Page Pool, failing to flush Modified Pages to disk, and failing to clear System File Caches.
2. **Disk Cleaning**: Legacy disk cleaning attempted raw `File.Delete` traversals inside `$Recycle.Bin` volumes (corrupting SID metadata and failing on volume locks), lacked attribute normalization (crashing with `UnauthorizedAccessException` on read-only files), dispatched synchronous UI updates per file (causing UI thread stutter), and omitted essential targets (Browser caches, Crash Dumps, WER).

Both subsystems have been completely redesigned and implemented using Windows Native NT APIs (`ntdll.dll`), Win32 Token Privilege Elevation (`advapi32.dll`), Shell APIs (`shell32.dll`), and non-blocking batch dispatching.

---

## 2. RAM Cleaner Deep Audit & NT API Architecture

### 2.1 Audit Findings & Justification for NT APIs

| Category | Legacy Implementation (`psapi.dll!EmptyWorkingSet`) | Optimized Implementation (`NativeMemoryService`) | Architectural Rationale |
|---|---|---|---|
| **Standby List Management** | **None**. Standby memory remained occupied (often 8GB–16GB+). | `NtSetSystemInformation(80, MemoryPurgeStandbyList)` | Purges all Standby Priority Lists (0–7), returning cached file pages directly to the Free/Zeroed page frame list (equivalent to Sysinternals RAMMap). |
| **Modified Page Management** | **None**. Dirty pages remained unwritten in RAM. | `NtSetSystemInformation(80, MemoryFlushModifiedList)` | Forces pending modified pages to disk, transitioning them into Standby pages before the purge pass. |
| **System Working Sets** | **None**. File system metadata cache remained unpruned. | `NtSetSystemInformation(80, MemoryEmptyWorkingSets)` | Trims kernel file cache working sets system-wide. |
| **Process Working Set Trimming** | Blindly trimmed processes. | Selective trimming with active foreground window and critical system process exclusion. | Prevents stutter or desktop freezing on active user applications and essential system services (`dwm`, `csrss`, `lsass`, `explorer`, `services`). |
| **Process Privilege Elevation** | **None**. Ran under standard process token privileges. | `AdjustTokenPrivileges` enabling `SeProfileSingleProcessPrivilege` and `SeIncreaseQuotaPrivilege`. | Mandated by Windows NT kernel to authorize low-level memory list purges. |

### 2.2 NT Kernel Memory List Commands & Constants

```csharp
public const int SystemMemoryListInformation = 80; // 0x50

public enum SystemMemoryListCommand
{
    MemoryCaptureAccessedBits = 0,
    MemoryCaptureAndResetMySQL = 1,
    MemoryEmptyWorkingSets = 2,           // Flush system & process working sets
    MemoryFlushModifiedList = 3,          // Flush modified pages to disk
    MemoryPurgeStandbyList = 4,           // Purge ALL standby lists (Priorities 0-7, RAMMap)
    MemoryPurgeLowPriorityStandbyList = 5 // Purge Priority 0 standby list
}
```

### 2.3 Memory Measurement & Delta Calculation

To eliminate guesswork and prevent fabricated statistics, `NativeMemoryService` queries the Windows NT Memory Manager via `GlobalMemoryStatusEx` immediately before and after execution:

$$\text{Freed RAM (Bytes)} = \max\left(0, \text{AvailPhys}_{T_2} - \text{AvailPhys}_{T_0}\right)$$
$$\Delta\text{Load Percent} = \text{dwMemoryLoad}_{T_0} - \text{dwMemoryLoad}_{T_2}$$

Where:
- $T_0$: Baseline measurement before privilege elevation and NT calls.
- $T_1$: Kernel execution of Flush Modified $\to$ Purge Standby $\to$ System Working Sets $\to$ Background Working Sets.
- $T_2$: Post-stabilization measurement after 60ms kernel page table convergence.

### 2.4 Safety Exclusions & Foreground Protection

To prevent desktop instability and high latency page faults on active user tasks:
1. **Critical System Exclusions**: `"System"`, `"svchost"`, `"dwm"`, `"csrss"`, `"smss"`, `"lsass"`, `"services"`, `"wininit"`, `"winlogon"`, `"explorer"`, `"spoolsv"`, `"taskmgr"`, `"MsMpEng"`, `"NisSrv"`, `"SecurityHealthService"`, `"smartscreen"`, `"SearchIndexer"`, `"sihost"`, `"fontdrvhost"`, `"WUDFHost"`, `"WmiPrvSE"`, `"audiodg"`, `"Registry"`, `"Memory Compression"`.
2. **Foreground Window Exclusion**: Calls `GetForegroundWindow()` and `GetWindowThreadProcessId()` to identify the user's active window and skip trimming its process.
3. **Session 0 Exclusion**: Background kernel service processes residing in Session 0 are skipped.

---

## 3. Disk Cleaner Safety Audit & Architecture

### 3.1 Audit Findings & Refactoring Rationale

1. **Recycle Bin Integrity via Win32 Shell API**:
   - *Issue*: Directly calling `File.Delete` in `$Recycle.Bin` breaks Windows shell SID metadata pairs (`$I*` index files and `$R*` data files) and causes `Access Denied` exceptions on Volume GUID partitions.
   - *Solution*: Replaced with `shell32.dll!SHEmptyRecycleBin(IntPtr.Zero, driveRoot, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND)`. This invokes the native Shell handler, properly invalidating desktop icons and cleaning volume indexes safely.

2. **Attribute Normalization for Read-Only Files**:
   - *Issue*: `File.Delete(path)` throws `UnauthorizedAccessException` on files marked `FileAttributes.ReadOnly`, `FileAttributes.Hidden`, or `FileAttributes.System`.
   - *Solution*: `SafeDeleteFile` checks attributes and resets them to `FileAttributes.Normal` via `File.SetAttributes(path, FileAttributes.Normal)` prior to calling `File.Delete`.

3. **Graceful Exception Isolation on Locked Files**:
   - *Issue*: Files locked with exclusive access (`FileShare.None`) by running services (e.g. active system logs or browser databases) crashed or halted file traversals.
   - *Solution*: Granular catch blocks categorize failures into `DeletionStatus.Locked` (`IOException`) and `DeletionStatus.AccessDenied` (`UnauthorizedAccessException`), continuing iteration without throwing unhandled exceptions.

4. **Expanded Cleaner Target Matrix**:
   - User Temp: `%TEMP%`, `%LOCALAPPDATA%\Temp`
   - System Temp: `%WINDIR%\Temp`
   - Windows Update Download Cache: `%WINDIR%\SoftwareDistribution\Download`
   - Crash Dumps: `%LOCALAPPDATA%\CrashDumps`
   - Windows Error Reporting: `%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive`, `ReportQueue`, `%PROGRAMDATA%\Microsoft\Windows\WER`
   - Browser Caches: Microsoft Edge, Google Chrome, Brave Browser, Mozilla Firefox (`cache2\entries`)
   - Empty directory cleanup: Bottom-up recursive deletion of empty child directories.

5. **Non-Blocking Batch UI Dispatch**:
   - *Issue*: Emitting individual `Dispatcher.Invoke` calls for every single deleted file froze the WPF rendering loop when handling 10,000+ files.
   - *Solution*: Decoupled traversal using `IProgress<DiskCleaningProgress>` throttled to ~40ms / 25 items intervals, keeping WPF 60 FPS responsive.

---

## 4. Verification & Attestation

### 4.1 Unit Test Coverage
The test suite in `PCHealthDashboard.Tests` validates:
- `GlobalMemoryStatusEx_ShouldReturnValidSystemMemoryMetrics`: Verifies accurate query of physical RAM and valid load percentage.
- `EnablePrivilege_SeProfileSingleProcess_ShouldNotThrow`: Verifies safe token privilege elevation.
- `NativeMemoryService_OptimizeRamDeep_ShouldReturnValidReport`: Verifies deep optimization flow and report generation.
- `NativeMemoryService_GetEligibleProcesses_ShouldExcludeBlacklistedProcesses`: Verifies exclusion of critical system and foreground processes.
- `SafeDeleteFile_NormalFile_ShouldDeleteSuccessfully`: Verifies deletion of standard files.
- `SafeDeleteFile_ReadOnlyFile_ShouldClearAttributesAndSucceed`: Verifies attribute normalization and deletion of read-only files.
- `SafeDeleteFile_LockedFile_ShouldGracefullyReturnLockedWithoutThrowing`: Verifies that locked files (`FileShare.None`) return `DeletionStatus.Locked` without throwing unhandled exceptions.
- `CleanSpecificFilesAsync_ShouldHandleMixedLockedAndNormalFiles`: Verifies batch execution over mixed normal, locked, and read-only files.

---

## 5. Artifact Manifest

All production-grade source code artifacts have been fully generated and verified:
1. `Helpers/NativeMethods.cs`
2. `Models/RamOptimizationReport.cs`
3. `Models/DiskCleaningReport.cs`
4. `Services/NativeMemoryService.cs`
5. `Services/DiskCleanerService.cs`
6. `ViewModels/RamOptimizerViewModel.cs`
7. `ViewModels/JunkCleanerViewModel.cs`
8. `Tests/RamCleanerTests.cs`
9. `Tests/DiskCleanerTests.cs`
10. `AUDIT_REPORT.md`
