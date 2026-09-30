using PCHealthDashboard.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PCHealthDashboard.Services;

public enum ProcessCloseResult
{
    Exited,
    NeedsForce,
    AccessDenied,
    ForceFailed,
    ProcessChanged
}

public sealed class ProcessMonitorService
{
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "registry", "idle", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "svchost", "fontdrvhost", "dwm"
    };

    private readonly object _sync = new();
    private readonly Dictionary<int, CpuSample> _cpuSamples = new();

    public IReadOnlyList<ProcessReading> GetSnapshot()
    {
        lock (_sync)
        {
            var now = Stopwatch.GetTimestamp();
            var seenProcessIds = new HashSet<int>();
            var readings = new List<ProcessReading>();

            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        var processId = process.Id;
                        var name = process.ProcessName;
                        var startTimeUtcTicks = GetStartTimeUtcTicks(process);
                        var processorTime = process.TotalProcessorTime;
                        var memoryMegabytes = Math.Max(0L, process.WorkingSet64) / (1024d * 1024d);
                        var cpuPercent = GetCpuPercent(processId, startTimeUtcTicks, processorTime, now);
                        var canTerminate = processId > 4 &&
                                           processId != Environment.ProcessId &&
                                           startTimeUtcTicks > 0 &&
                                           !ProtectedProcessNames.Contains(name);

                        seenProcessIds.Add(processId);
                        readings.Add(new ProcessReading(
                            processId,
                            name,
                            cpuPercent,
                            memoryMegabytes,
                            startTimeUtcTicks,
                            canTerminate));
                    }
                    catch (Exception exception) when (exception is InvalidOperationException
                                                       or System.ComponentModel.Win32Exception
                                                       or UnauthorizedAccessException
                                                       or NotSupportedException)
                    {
                        // Processes can exit or become inaccessible during enumeration.
                    }
                }
            }

            foreach (var staleProcessId in _cpuSamples.Keys.Where(id => !seenProcessIds.Contains(id)).ToArray())
                _cpuSamples.Remove(staleProcessId);

            // Stable source order keeps the UI from reshuffling rows every poll.
            return readings
                .OrderBy(reading => reading.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(reading => reading.ProcessId)
                .ToArray();
        }
    }

    public async Task<ProcessCloseResult> RequestCloseAsync(ProcessEntry entry)
    {
        if (!entry.CanTerminate)
            return ProcessCloseResult.ProcessChanged;

        using var process = OpenVerifiedProcess(entry, out var result);
        if (process == null)
            return result;

        try
        {
            if (!process.CloseMainWindow())
                return ProcessCloseResult.NeedsForce;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return ProcessCloseResult.Exited;
        }
        catch (OperationCanceledException)
        {
            return ProcessCloseResult.NeedsForce;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return ProcessCloseResult.AccessDenied;
        }
        catch (InvalidOperationException)
        {
            return ProcessCloseResult.Exited;
        }
    }

    public async Task<ProcessCloseResult> ForceTerminateAsync(ProcessEntry entry)
    {
        if (!entry.CanTerminate)
            return ProcessCloseResult.ProcessChanged;

        using var process = OpenVerifiedProcess(entry, out var result);
        if (process == null)
            return result;

        try
        {
            process.Kill();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return ProcessCloseResult.Exited;
        }
        catch (OperationCanceledException)
        {
            return ProcessCloseResult.ForceFailed;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return ProcessCloseResult.AccessDenied;
        }
        catch (InvalidOperationException)
        {
            return ProcessCloseResult.Exited;
        }
    }

    private float GetCpuPercent(int processId, long startTimeUtcTicks, TimeSpan processorTime, long now)
    {
        var cpuPercent = 0d;
        if (startTimeUtcTicks > 0 && _cpuSamples.TryGetValue(processId, out var previous) &&
            previous.StartTimeUtcTicks == startTimeUtcTicks)
        {
            var elapsedSeconds = Stopwatch.GetElapsedTime(previous.Timestamp, now).TotalSeconds;
            if (elapsedSeconds > 0d)
            {
                var processorSeconds = (processorTime.Ticks - previous.ProcessorTimeTicks) / (double)TimeSpan.TicksPerSecond;
                cpuPercent = Math.Clamp(processorSeconds / elapsedSeconds / Environment.ProcessorCount * 100d, 0d, 100d);
            }
        }

        _cpuSamples[processId] = new CpuSample(startTimeUtcTicks, processorTime.Ticks, now);
        return (float)cpuPercent;
    }

    private static long GetStartTimeUtcTicks(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                           or System.ComponentModel.Win32Exception
                                           or UnauthorizedAccessException
                                           or NotSupportedException)
        {
            return 0;
        }
    }

    private static Process? OpenVerifiedProcess(ProcessEntry entry, out ProcessCloseResult result)
    {
        try
        {
            var process = Process.GetProcessById(entry.ProcessId);
            if (process.HasExited)
            {
                process.Dispose();
                result = ProcessCloseResult.Exited;
                return null;
            }

            var actualStartTime = GetStartTimeUtcTicks(process);
            if (actualStartTime == 0 || Math.Abs(actualStartTime - entry.StartTimeUtcTicks) > TimeSpan.TicksPerSecond)
            {
                process.Dispose();
                result = ProcessCloseResult.ProcessChanged;
                return null;
            }

            result = ProcessCloseResult.Exited;
            return process;
        }
        catch (ArgumentException)
        {
            result = ProcessCloseResult.Exited;
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            result = ProcessCloseResult.AccessDenied;
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            result = ProcessCloseResult.AccessDenied;
            return null;
        }
        catch (InvalidOperationException)
        {
            result = ProcessCloseResult.Exited;
            return null;
        }
    }

    private readonly record struct CpuSample(long StartTimeUtcTicks, long ProcessorTimeTicks, long Timestamp);
}
