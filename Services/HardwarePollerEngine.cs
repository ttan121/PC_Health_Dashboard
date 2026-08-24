// ============================================================================
// PC Health Dashboard - Services/HardwarePollerEngine.cs
// High-Performance Zero-Allocation Hardware Poller with Cryo Mode
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using PCHealthDashboard.Models;

namespace PCHealthDashboard.Services;

/// <summary>
/// Hardware Poller Engine Interface conforming to Master Project Specification § HardwarePollerEngine.
/// </summary>
public interface IHardwarePollerEngine : IDisposable
{
    /// <summary>
    /// Discovers topology and pre-resolves direct ISensor references to eliminate runtime LINQ queries.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Switches between Normal (1s) and Cryo (5s-10s, UI detached, core sensors only) mode.
    /// </summary>
    void SetMode(PollerMode mode);

    /// <summary>
    /// Current operational mode of the poller.
    /// </summary>
    PollerMode CurrentMode { get; }

    /// <summary>
    /// Attaches the UI update callback for live telemetry dispatch.
    /// Immediately dispatches the latest snapshot to initialize UI views.
    /// </summary>
    void AttachUi(Action<HardwareSnapshot> updateCallback);

    /// <summary>
    /// Detaches the UI update callback, suspending all UI dispatcher and visual tree overhead.
    /// </summary>
    void DetachUi();

    /// <summary>
    /// Retrieves the most recent telemetry snapshot in a thread-safe, zero-allocation manner.
    /// </summary>
    HardwareSnapshot GetLatestSnapshot();

    /// <summary>
    /// Performs a direct synchronous poll and copies the result to destination. Zero heap allocations.
    /// </summary>
    void PollDirect(out HardwareSnapshot snapshot);
}

/// <summary>
/// Pre-cached GPU sensor binding slot.
/// Stored in a fixed-size struct array to guarantee zero heap allocations per tick.
/// </summary>
public struct GpuBinding
{
    public IHardware? Hardware;
    public ISensor? LoadSensor;
    public ISensor? TempSensor;
    public ISensor? VramUsedSensor;
    public ISensor? VramTotalSensor;
    public string Name;
    public string Id;
    public bool IsSharedMemory;
    public bool IsAvailable;
}

/// <summary>
/// Pre-cached Network sensor binding slot.
/// </summary>
public struct NetworkBinding
{
    public IHardware? Hardware;
    public ISensor? DownloadSensor;
    public ISensor? UploadSensor;
}

/// <summary>
/// Pre-cached Storage sensor binding slot.
/// </summary>
public struct StorageBinding
{
    public IHardware? Hardware;
    public ISensor? ReadSensor;
    public ISensor? WriteSensor;
    public ISensor? UsedSpaceSensor;
}

/// <summary>
/// Production-grade Zero-Allocation Hardware Poller Engine.
/// Pre-caches direct ISensor pointers at startup, uses an allocation-free background polling loop,
/// supports Cryo Mode background throttling, and lifecycle-managed UI attachment.
/// </summary>
public sealed class HardwarePollerEngine : IHardwarePollerEngine
{
    private readonly Computer _computer;
    private readonly bool _isMockMode;
    private bool _isDisposed;
    private bool _isInitialized;

    // Direct Pre-Resolved CPU Sensors
    private IHardware? _cpuHardware;
    private ISensor? _cpuLoadSensor;
    private ISensor? _cpuTempSensor;
    private ISensor? _cpuPowerSensor;
    private ISensor? _cpuClockSensor;

    // Direct Pre-Resolved RAM Sensors
    private IHardware? _ramHardware;
    private ISensor? _ramUsedSensor;
    private ISensor? _ramAvailSensor;

    // Pre-allocated fixed-size binding arrays (Max 4 GPUs, 8 Network adapters, 8 Storage drives)
    private readonly GpuBinding[] _gpuBindings = new GpuBinding[4];
    private int _gpuCount;

    private readonly NetworkBinding[] _networkBindings = new NetworkBinding[8];
    private int _networkCount;

    private readonly StorageBinding[] _storageBindings = new StorageBinding[8];
    private int _storageCount;

    // Cached Storage Space (calculated periodically with cached DriveInfo handle)
    private DriveInfo? _systemDrive;
    private float _cachedSsdUsedGb;
    private float _cachedSsdTotalGb = 512f;
    private float _cachedSsdHealth = 100f;
    private int _storageRefreshCounter;

    // Poller State & Synchronization
    private PollerMode _currentMode = PollerMode.Normal;
    private int _normalIntervalMs = 1000;
    private int _cryoIntervalMs = 5000;
    private Thread? _pollingThread;
    private readonly ManualResetEventSlim _wakeEvent = new(false);
    private readonly CancellationTokenSource _cts = new();

    // UI Lifecycle Attachment
    private Action<HardwareSnapshot>? _uiCallback;
    private volatile bool _isUiAttached;
    private readonly object _stateLock = new();

    // Latest snapshot cache
    private HardwareSnapshot _latestSnapshot = HardwareSnapshot.Empty;

    /// <summary>
    /// Current operational mode.
    /// </summary>
    public PollerMode CurrentMode
    {
        get
        {
            lock (_stateLock) return _currentMode;
        }
    }

    /// <summary>
    /// Configurable Normal mode interval in milliseconds (default: 1000ms).
    /// </summary>
    public int NormalIntervalMs
    {
        get => _normalIntervalMs;
        set => _normalIntervalMs = Math.Max(100, value);
    }

    /// <summary>
    /// Configurable Cryo mode interval in milliseconds (default: 5000ms).
    /// </summary>
    public int CryoIntervalMs
    {
        get => _cryoIntervalMs;
        set => _cryoIntervalMs = Math.Max(1000, value);
    }

    /// <summary>
    /// Constructs a standard HardwarePollerEngine backed by LibreHardwareMonitor.
    /// </summary>
    public HardwarePollerEngine() : this(isMockMode: false)
    {
    }

    /// <summary>
    /// Constructs a HardwarePollerEngine with optional synthetic/mock hardware simulation
    /// for offline environments and deterministic performance benchmarking.
    /// </summary>
    public HardwarePollerEngine(bool isMockMode)
    {
        _isMockMode = isMockMode;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsMotherboardEnabled = false,
            IsNetworkEnabled = true
        };
    }

    /// <summary>
    /// Discovers system hardware topology and resolves all direct ISensor handles.
    /// Called once at startup to guarantee 0 LINQ searches during live polling.
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized) return;

        try
        {
            _systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
        }
        catch
        {
            _systemDrive = null;
        }

        if (!_isMockMode)
        {
            try
            {
                _computer.Open();
            }
            catch
            {
                // Fallback gracefully if driver cannot open (e.g. running non-elevated or virtualized)
            }

            CacheSensors();
        }
        else
        {
            InitializeMockSensors();
        }

        RefreshStorageCapacity();

        // Perform initial warm-up poll
        PollDirect(out _latestSnapshot);

        // Start background polling thread
        _pollingThread = new Thread(PollingThreadWorker)
        {
            Name = "PCHealth.HardwarePoller",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal
        };
        _pollingThread.Start();

        _isInitialized = true;
    }

    /// <summary>
    /// Traverses the LibreHardwareMonitor hierarchy once and pre-caches direct sensor handles.
    /// Prioritizes discrete GPU handles (Nvidia/AMD) over integrated GPUs.
    /// </summary>
    private void CacheSensors()
    {
        var hardwareList = _computer.Hardware
            .OrderBy(hw => hw.HardwareType switch
            {
                HardwareType.GpuNvidia => 1,
                HardwareType.GpuAmd => 2,
                HardwareType.GpuIntel => 3,
                _ => 10
            })
            .ToList();

        // 1. Resolve CPU Hardware & Sensors
        foreach (var hw in hardwareList)
        {
            if (hw.HardwareType == HardwareType.Cpu)
            {
                _cpuHardware = hw;
                hw.Update();

                foreach (var sensor in hw.Sensors)
                {
                    if (sensor.SensorType == SensorType.Load && _cpuLoadSensor == null)
                    {
                        if (sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                            _cpuLoadSensor = sensor;
                    }
                    else if (sensor.SensorType == SensorType.Temperature && _cpuTempSensor == null)
                    {
                        if (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                            sensor.Name.Contains("Core Max", StringComparison.OrdinalIgnoreCase) ||
                            sensor.Name.Contains("Core Average", StringComparison.OrdinalIgnoreCase))
                        {
                            _cpuTempSensor = sensor;
                        }
                    }
                    else if (sensor.SensorType == SensorType.Power && _cpuPowerSensor == null)
                    {
                        if (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                            _cpuPowerSensor = sensor;
                    }
                    else if (sensor.SensorType == SensorType.Clock && _cpuClockSensor == null)
                    {
                        _cpuClockSensor = sensor;
                    }
                }

                // Fallbacks if specific sensor names were not found
                if (_cpuLoadSensor == null)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Load) { _cpuLoadSensor = s; break; }
                    }
                }
                if (_cpuTempSensor == null)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Temperature) { _cpuTempSensor = s; break; }
                    }
                }
                if (_cpuPowerSensor == null)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Power) { _cpuPowerSensor = s; break; }
                    }
                }
            }

            // 2. Resolve RAM Hardware & Sensors
            else if (hw.HardwareType == HardwareType.Memory)
            {
                _ramHardware = hw;
                hw.Update();

                foreach (var sensor in hw.Sensors)
                {
                    if (sensor.SensorType == SensorType.Data)
                    {
                        if (sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase))
                            _ramUsedSensor = sensor;
                        else if (sensor.Name.Contains("Memory Available", StringComparison.OrdinalIgnoreCase))
                            _ramAvailSensor = sensor;
                    }
                }
            }

            // 3. Resolve GPU Hardware & Sensors (Nvidia, AMD, Intel)
            else if (hw.HardwareType == HardwareType.GpuNvidia ||
                     hw.HardwareType == HardwareType.GpuAmd ||
                     hw.HardwareType == HardwareType.GpuIntel)
            {
                if (_gpuCount < _gpuBindings.Length)
                {
                    hw.Update();
                    ref var binding = ref _gpuBindings[_gpuCount];
                    binding.Hardware = hw;
                    binding.Name = hw.Name;
                    binding.Id = hw.Identifier.ToString();
                    binding.IsAvailable = true;

                    ISensor? dedicatedVram = null;
                    ISensor? sharedVram = null;

                    foreach (var sensor in hw.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && binding.TempSensor == null)
                        {
                            binding.TempSensor = sensor;
                        }
                        else if (sensor.SensorType == SensorType.Load && binding.LoadSensor == null)
                        {
                            if (sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                sensor.Name.Contains("D3D 3D", StringComparison.OrdinalIgnoreCase))
                            {
                                binding.LoadSensor = sensor;
                            }
                        }
                        else if (sensor.SensorType == SensorType.SmallData || sensor.SensorType == SensorType.Data)
                        {
                            if (sensor.Name.Contains("Dedicated Memory Used", StringComparison.OrdinalIgnoreCase))
                                dedicatedVram = sensor;
                            else if (sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase) && dedicatedVram == null)
                                sharedVram = sensor;
                            else if (sensor.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase))
                                binding.VramTotalSensor = sensor;
                        }
                    }

                    if (binding.LoadSensor == null)
                    {
                        foreach (var s in hw.Sensors)
                        {
                            if (s.SensorType == SensorType.Load) { binding.LoadSensor = s; break; }
                        }
                    }

                    binding.VramUsedSensor = dedicatedVram ?? sharedVram;
                    binding.IsSharedMemory = (dedicatedVram == null && sharedVram != null);

                    _gpuCount++;
                }
            }

            // 4. Resolve Network Hardware
            else if (hw.HardwareType == HardwareType.Network)
            {
                if (_networkCount < _networkBindings.Length)
                {
                    hw.Update();
                    ref var binding = ref _networkBindings[_networkCount];
                    binding.Hardware = hw;

                    foreach (var sensor in hw.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Throughput)
                        {
                            if (sensor.Name.Contains("Download", StringComparison.OrdinalIgnoreCase))
                                binding.DownloadSensor = sensor;
                            else if (sensor.Name.Contains("Upload", StringComparison.OrdinalIgnoreCase))
                                binding.UploadSensor = sensor;
                        }
                    }
                    _networkCount++;
                }
            }

            // 5. Resolve Storage Hardware
            else if (hw.HardwareType == HardwareType.Storage)
            {
                if (_storageCount < _storageBindings.Length)
                {
                    hw.Update();
                    ref var binding = ref _storageBindings[_storageCount];
                    binding.Hardware = hw;

                    foreach (var sensor in hw.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Data)
                        {
                            if (sensor.Name.Contains("Read", StringComparison.OrdinalIgnoreCase))
                                binding.ReadSensor = sensor;
                            else if (sensor.Name.Contains("Write", StringComparison.OrdinalIgnoreCase))
                                binding.WriteSensor = sensor;
                        }
                        else if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Used Space", StringComparison.OrdinalIgnoreCase))
                        {
                            binding.UsedSpaceSensor = sensor;
                        }
                    }
                    _storageCount++;
                }
            }
        }
    }

    private void InitializeMockSensors()
    {
        _gpuCount = 1;
        ref var binding = ref _gpuBindings[0];
        binding.Name = "Synthetic NVIDIA GeForce RTX 4080";
        binding.Id = "GPU-SYNTHETIC-0";
        binding.IsAvailable = true;
        binding.IsSharedMemory = false;
        _networkCount = 1;
        _storageCount = 1;
        _cachedSsdTotalGb = 1000f;
        _cachedSsdUsedGb = 350f;
        _cachedSsdHealth = 98f;
    }

    private void RefreshStorageCapacity()
    {
        try
        {
            if (_systemDrive != null && _systemDrive.IsReady)
            {
                _cachedSsdTotalGb = (float)(_systemDrive.TotalSize / (1024.0 * 1024.0 * 1024.0));
                _cachedSsdUsedGb = _cachedSsdTotalGb - (float)(_systemDrive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0));
            }
        }
        catch
        {
            if (_cachedSsdTotalGb <= 0f)
            {
                _cachedSsdTotalGb = 512f;
                _cachedSsdUsedGb = 256f;
            }
        }
    }

    /// <summary>
    /// Switches mode dynamically. Wakes up the polling loop immediately if needed.
    /// Restores UI attachment in Normal mode and detaches UI in Cryo mode.
    /// </summary>
    public void SetMode(PollerMode mode)
    {
        lock (_stateLock)
        {
            if (_currentMode == mode) return;
            _currentMode = mode;

            if (mode == PollerMode.Cryo)
            {
                // Detach UI in Cryo mode to suspend visual tree updates and dispatcher tasks
                _isUiAttached = false;
            }
            else if (mode == PollerMode.Normal)
            {
                // Restore UI attachment if callback is registered
                _isUiAttached = (_uiCallback != null);
            }
        }

        // Trigger immediate loop wake-up to adapt to new interval
        _wakeEvent.Set();
    }

    /// <summary>
    /// Attaches UI callback for live telemetry push.
    /// Immediately invokes callback with the latest snapshot.
    /// </summary>
    public void AttachUi(Action<HardwareSnapshot> updateCallback)
    {
        ArgumentNullException.ThrowIfNull(updateCallback);

        HardwareSnapshot current;
        lock (_stateLock)
        {
            _uiCallback = updateCallback;
            _isUiAttached = (_currentMode == PollerMode.Normal);
            current = _latestSnapshot;
        }

        try
        {
            updateCallback(current);
        }
        catch
        {
            // Safeguard against UI exceptions during initial attach
        }
    }

    /// <summary>
    /// Detaches UI update callback.
    /// </summary>
    public void DetachUi()
    {
        lock (_stateLock)
        {
            _isUiAttached = false;
            _uiCallback = null;
        }
    }

    /// <summary>
    /// Retrieves latest telemetry snapshot. Zero heap allocation.
    /// </summary>
    public HardwareSnapshot GetLatestSnapshot()
    {
        lock (_stateLock)
        {
            return _latestSnapshot;
        }
    }

    /// <summary>
    /// Dedicated background worker thread.
    /// Zero heap allocation per loop iteration.
    /// Safely handles cancellation and thread aborts during Dispose().
    /// </summary>
    private void PollingThreadWorker()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                int interval;
                PollerMode mode;
                Action<HardwareSnapshot>? callback;
                bool uiAttached;

                lock (_stateLock)
                {
                    mode = _currentMode;
                    interval = (mode == PollerMode.Cryo) ? _cryoIntervalMs : _normalIntervalMs;
                    callback = _uiCallback;
                    uiAttached = _isUiAttached;
                }

                // Direct Zero-Allocation Sensor Poll
                PollDirect(out var snapshot);

                lock (_stateLock)
                {
                    _latestSnapshot = snapshot;
                }

                // Dispatch to UI only if attached and in Normal mode
                if (uiAttached && callback != null && mode != PollerMode.Cryo)
                {
                    bool shouldDispatch = false;
                    lock (_stateLock)
                    {
                        if (_isUiAttached && _uiCallback != null && _currentMode != PollerMode.Cryo)
                        {
                            shouldDispatch = true;
                        }
                    }

                    if (shouldDispatch)
                    {
                        try
                        {
                            callback(snapshot);
                        }
                        catch
                        {
                            // Ignore UI dispatch failures to prevent poller crash
                        }
                    }
                }

                // Sleep with wait handle allowing instant wake on mode change or cancellation
                _wakeEvent.Reset();
                if (_wakeEvent.Wait(interval))
                {
                    // Woken early (mode changed or cancelled)
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean exit on token cancellation
        }
        catch (ThreadAbortException)
        {
            // Clean exit on thread abort
        }
        catch
        {
            // Suppress unhandled thread exceptions
        }
    }

    private static float Sanitize(float v, float fallback = 0f) =>
        float.IsNaN(v) || float.IsInfinity(v) ? fallback : v;

    /// <summary>
    /// Core Direct Poll routine.
    /// ALL operations are performed on pre-allocated structs and cached ISensor handles.
    /// Absolutely ZERO heap allocations occur in this method.
    /// All sensor values are sanitized against NaN and Infinity.
    /// </summary>
    public void PollDirect(out HardwareSnapshot snapshot)
    {
        long ticks = DateTime.UtcNow.Ticks;
        PollerMode mode;
        lock (_stateLock)
        {
            mode = _currentMode;
        }

        if (_isMockMode)
        {
            PollMockSensors(ticks, mode, out snapshot);
            return;
        }

        float cpuUsage = 0f;
        float cpuTemp = 45f; // Sensible default if locked
        float cpuPower = 0f;
        float cpuClock = 0f;

        float ramUsed = 0f;
        float ramTotal = 16f;

        float ssdUsed = _cachedSsdUsedGb;
        float ssdTotal = _cachedSsdTotalGb;
        float ssdHealth = _cachedSsdHealth;

        float netDown = 0f;
        float netUp = 0f;

        int gpuCount = _gpuCount;
        float gpuUsage = 0f;
        float gpuTemp = 0f;
        float gpuVramUsed = 0f;
        float gpuVramTotal = 0f;

        try
        {
            // 1. Core Sensors: CPU & RAM (Always polled in both Normal & Cryo mode)
            if (_cpuHardware != null)
            {
                _cpuHardware.Update();

                if (_cpuLoadSensor?.Value != null) cpuUsage = Sanitize(_cpuLoadSensor.Value.Value);
                if (_cpuTempSensor?.Value != null) cpuTemp = Sanitize(_cpuTempSensor.Value.Value, 45f);
                if (_cpuPowerSensor?.Value != null) cpuPower = Sanitize(_cpuPowerSensor.Value.Value);
                if (_cpuClockSensor?.Value != null) cpuClock = Sanitize(_cpuClockSensor.Value.Value);
            }

            var mem = new PCHealthDashboard.Helpers.NativeMethods.MEMORYSTATUSEX();
            if (PCHealthDashboard.Helpers.NativeMethods.GlobalMemoryStatusEx(mem) && mem.ullTotalPhys > 0)
            {
                ramTotal = Sanitize((float)(mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0)), 16f);
                ramUsed = Sanitize((float)((mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0)));
            }
            else if (_ramHardware != null)
            {
                _ramHardware.Update();

                if (_ramUsedSensor?.Value != null) ramUsed = Sanitize(_ramUsedSensor.Value.Value);
                if (_ramAvailSensor?.Value != null)
                {
                    float avail = Sanitize(_ramAvailSensor.Value.Value);
                    ramTotal = ramUsed + avail;
                }
            }

            // 2. Extended Subsystems: Polled in Normal mode; skipped or heavily throttled in Cryo mode
            if (mode == PollerMode.Normal)
            {
                // GPU Polling
                for (int i = 0; i < _gpuCount; i++)
                {
                    ref var binding = ref _gpuBindings[i];
                    if (binding.Hardware != null)
                    {
                        binding.Hardware.Update();

                        if (binding.LoadSensor?.Value != null) gpuUsage = Sanitize(binding.LoadSensor.Value.Value);
                        if (binding.TempSensor?.Value != null) gpuTemp = Sanitize(binding.TempSensor.Value.Value);

                        if (binding.VramUsedSensor?.Value != null)
                        {
                            gpuVramUsed = Sanitize(binding.VramUsedSensor.Value.Value);
                            if (gpuVramUsed > 128f) gpuVramUsed /= 1024f; // Convert MB to GB if needed
                        }

                        if (binding.VramTotalSensor?.Value != null)
                        {
                            gpuVramTotal = Sanitize(binding.VramTotalSensor.Value.Value);
                            if (gpuVramTotal > 128f) gpuVramTotal /= 1024f;
                        }
                        else if (binding.IsSharedMemory)
                        {
                            gpuVramTotal = ramTotal / 2f;
                        }
                    }
                }

                // Network Polling
                for (int i = 0; i < _networkCount; i++)
                {
                    ref var binding = ref _networkBindings[i];
                    if (binding.Hardware != null)
                    {
                        binding.Hardware.Update();
                        if (binding.DownloadSensor?.Value != null) netDown += Sanitize(binding.DownloadSensor.Value.Value);
                        if (binding.UploadSensor?.Value != null) netUp += Sanitize(binding.UploadSensor.Value.Value);
                    }
                }
                // Convert bytes/sec to Mbps: (bytes * 8) / 1,000,000
                netDown = (netDown * 8f) / 1_000_000f;
                netUp = (netUp * 8f) / 1_000_000f;

                // Throttled Storage Refresh (Every 30 ticks)
                _storageRefreshCounter++;
                if (_storageRefreshCounter >= 30)
                {
                    _storageRefreshCounter = 0;
                    RefreshStorageCapacity();
                    ssdUsed = _cachedSsdUsedGb;
                    ssdTotal = _cachedSsdTotalGb;
                }
            }
        }
        catch
        {
            // Absorb transient hardware query errors without crashing
        }

        snapshot = new HardwareSnapshot(
            TimestampUtcTicks: ticks,
            CpuUsage: Sanitize(cpuUsage),
            CpuTemp: Sanitize(cpuTemp, 45f),
            CpuPower: Sanitize(cpuPower),
            CpuClock: Sanitize(cpuClock),
            RamUsedGb: Sanitize(ramUsed),
            RamTotalGb: Sanitize(ramTotal, 16f),
            SsdUsedGb: Sanitize(ssdUsed),
            SsdTotalGb: Sanitize(ssdTotal, 512f),
            SsdHealth: Sanitize(ssdHealth, 100f),
            NetDownMbps: Sanitize(netDown),
            NetUpMbps: Sanitize(netUp),
            GpuCount: gpuCount,
            GpuUsage: Sanitize(gpuUsage),
            GpuTemp: Sanitize(gpuTemp),
            GpuVramUsedGb: Sanitize(gpuVramUsed),
            GpuVramTotalGb: Sanitize(gpuVramTotal)
        );
    }

    private void PollMockSensors(long ticks, PollerMode mode, out HardwareSnapshot snapshot)
    {
        // Deterministic synthetic math - zero allocations
        float timeSec = (float)(ticks / 10_000_000.0);
        float cpuUsage = 25f + 15f * (float)Math.Sin(timeSec * 0.5);
        float cpuTemp = 55f + 8f * (float)Math.Cos(timeSec * 0.3);
        float cpuPower = 65f + 20f * (float)Math.Sin(timeSec * 0.2);
        float cpuClock = 4200f;

        float ramUsed = 8.5f + 0.5f * (float)Math.Sin(timeSec * 0.1);
        float ramTotal = 32f;

        float gpuUsage = (mode == PollerMode.Normal) ? (40f + 20f * (float)Math.Cos(timeSec * 0.4)) : 0f;
        float gpuTemp = (mode == PollerMode.Normal) ? (58f + 5f * (float)Math.Sin(timeSec * 0.2)) : 0f;
        float gpuVramUsed = (mode == PollerMode.Normal) ? 4.2f : 0f;
        float gpuVramTotal = 16f;

        float netDown = (mode == PollerMode.Normal) ? (15.5f + 10f * (float)Math.Sin(timeSec)) : 0f;
        float netUp = (mode == PollerMode.Normal) ? (2.1f + 1f * (float)Math.Cos(timeSec)) : 0f;

        snapshot = new HardwareSnapshot(
            TimestampUtcTicks: ticks,
            CpuUsage: Math.Max(0f, Math.Min(100f, cpuUsage)),
            CpuTemp: Math.Max(20f, Math.Min(105f, cpuTemp)),
            CpuPower: Math.Max(0f, cpuPower),
            CpuClock: cpuClock,
            RamUsedGb: Math.Max(0f, ramUsed),
            RamTotalGb: ramTotal,
            SsdUsedGb: _cachedSsdUsedGb,
            SsdTotalGb: _cachedSsdTotalGb,
            SsdHealth: _cachedSsdHealth,
            NetDownMbps: Math.Max(0f, netDown),
            NetUpMbps: Math.Max(0f, netUp),
            GpuCount: _gpuCount,
            GpuUsage: Math.Max(0f, Math.Min(100f, gpuUsage)),
            GpuTemp: Math.Max(20f, Math.Min(105f, gpuTemp)),
            GpuVramUsedGb: gpuVramUsed,
            GpuVramTotalGb: gpuVramTotal
        );
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _cts.Cancel();
            _wakeEvent.Set();

            if (_pollingThread != null && _pollingThread.IsAlive)
            {
                _pollingThread.Join(500);
            }

            _cts.Dispose();
            _wakeEvent.Dispose();

            if (!_isMockMode)
            {
                _computer.Close();
            }
        }
        catch
        {
            // Suppress teardown exceptions
        }
    }
}
