using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;
using PCHealthDashboard.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;

namespace PCHealthDashboard.Services;

public class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer)
    {
        computer.Traverse(this);
    }
    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (IHardware subHardware in hardware.SubHardware) subHardware.Accept(this);
    }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}

public class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _updateVisitor;
    private IHardware? _cpu;
    private readonly List<IHardware> _gpus = new();
    private IHardware? _ram;
    private DateTime _nextDellCpuTemperatureReadUtc = DateTime.MinValue;
    private float _cachedDellCpuTemperature;
    private readonly float[] _cpuTemperatureSamples = new float[5];
    private int _cpuTemperatureSampleCount;
    private int _cpuTemperatureSampleIndex;
    private float _smoothedCpuTemperature;
    private long _lastCpuTemperatureTimestamp;

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = false,
            IsMotherboardEnabled = false,
            IsNetworkEnabled = true
        };
        
        _updateVisitor = new UpdateVisitor();

        try { _computer.Open(); } catch { }

        InitializeHardware();
    }

    private void InitializeHardware()
    {
        _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        
        var gpus = _computer.Hardware.Where(h => 
            h.HardwareType == HardwareType.GpuNvidia || 
            h.HardwareType == HardwareType.GpuAmd || 
            h.HardwareType == HardwareType.GpuIntel).ToList();
            
        foreach (var gpu in gpus)
        {
            _gpus.Add(gpu);
        }

        _ram = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
    }

    private int _updateCounter = 0;
    private readonly object _syncLock = new();

    public void Update(bool fullUpdate = true)
    {
        lock (_syncLock)
        {
            _updateCounter++;
            if (_cpu != null)
            {
                try { _updateVisitor.VisitHardware(_cpu); } catch { }
            }

            if (_ram != null)
            {
                try { _updateVisitor.VisitHardware(_ram); } catch { }
            }

            foreach (var gpu in _gpus)
            {
                try { _updateVisitor.VisitHardware(gpu); } catch { }
            }

            // Only full update storage/network every 3 ticks to save CPU
            if (fullUpdate || _updateCounter % 3 == 0)
            {
                foreach (var hw in _computer.Hardware)
                {
                    if (hw.HardwareType == HardwareType.Storage || hw.HardwareType == HardwareType.Network)
                    {
                        try { _updateVisitor.VisitHardware(hw); } catch { }
                    }
                }
            }
        }
    }

    public (float usage, float temp, float displayTemp, float rawTemp, string tempSource, float power, float clock) GetCpuStats()
    {
        lock (_syncLock)
        {
            float load = 0f, rawTemp = 0f, power = 0f, clock = 0f;
            string tempSource = string.Empty;
            if (_cpu != null)
            {
                var sensors = EnumerateSensors(_cpu).ToList();
                var loadSensor = sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase));
                var tempSensor = FindCpuTemperatureSensor(sensors);
                var powerSensor = sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase));
                var clockSensor = sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock);

                if (loadSensor?.Value != null) load = loadSensor.Value.Value;
                rawTemp = ReadTemperature(tempSensor);
                if (rawTemp > 0f)
                    tempSource = tempSensor?.Name ?? string.Empty;
                else
                {
                    rawTemp = ReadDellCpuTemperature();
                    if (rawTemp > 0f)
                        tempSource = "Dell BIOS sensor";
                }
                if (powerSensor?.Value != null) power = powerSensor.Value.Value;
                if (clockSensor?.Value != null) clock = clockSensor.Value.Value;
            }

            var temp = FilterCpuTemperature(rawTemp);
            var displayTemp = SmoothCpuTemperature(temp, rawTemp);
            return (load, temp, displayTemp, rawTemp, tempSource, power, clock);
        }
    }

    private static ISensor? FindCpuTemperatureSensor(IReadOnlyCollection<ISensor> sensors)
    {
        var validSensors = sensors
            .Where(sensor => sensor.SensorType == SensorType.Temperature && ReadTemperature(sensor) > 0f)
            .ToList();

        // Prefer a package/die-wide sensor over an individual core. Core 0 can
        // briefly spike while the package average remains stable.
        var packageSensor = validSensors.FirstOrDefault(sensor =>
            sensor.Name.Contains("CPU Package", StringComparison.OrdinalIgnoreCase))
            ?? validSensors.FirstOrDefault(sensor =>
                sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase));
        if (packageSensor != null)
            return packageSensor;

        var rankedSensor = validSensors
            .Select(sensor => new { Sensor = sensor, Rank = GetCpuTemperatureSensorRank(sensor.Name) })
            .Where(candidate => candidate.Rank < 4)
            .OrderBy(candidate => candidate.Rank)
            .ThenByDescending(candidate => ReadTemperature(candidate.Sensor))
            .Select(candidate => candidate.Sensor)
            .FirstOrDefault();
        if (rankedSensor != null)
            return rankedSensor;

        // Firmware that exposes only per-core values: report the hottest core
        // instead of whichever core happens to be listed first.
        return validSensors
            .Where(sensor => sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ReadTemperature)
            .FirstOrDefault()
            ?? validSensors.OrderByDescending(ReadTemperature).FirstOrDefault();
    }

    private static int GetCpuTemperatureSensorRank(string name)
    {
        if (name.Contains("Tctl/Tdie", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Tdie", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.Contains("Core Max", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (name.Contains("Core Average", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Die (Average)", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 4;
    }

    private float FilterCpuTemperature(float rawTemperature)
    {
        if (rawTemperature <= 0f)
        {
            _cpuTemperatureSampleCount = 0;
            _cpuTemperatureSampleIndex = 0;
            _smoothedCpuTemperature = 0f;
            _lastCpuTemperatureTimestamp = 0;
            return 0f;
        }

        _cpuTemperatureSamples[_cpuTemperatureSampleIndex] = rawTemperature;
        _cpuTemperatureSampleIndex = (_cpuTemperatureSampleIndex + 1) % _cpuTemperatureSamples.Length;
        _cpuTemperatureSampleCount = Math.Min(_cpuTemperatureSampleCount + 1, _cpuTemperatureSamples.Length);

        Span<float> samples = stackalloc float[_cpuTemperatureSampleCount];
        for (var index = 0; index < _cpuTemperatureSampleCount; index++)
            samples[index] = _cpuTemperatureSamples[index];
        samples.Sort();

        return _cpuTemperatureSampleCount switch
        {
            1 => samples[0],
            2 => (samples[0] + samples[1]) / 2f,
            _ => samples[1]
        };
    }

    private float SmoothCpuTemperature(float filteredTemperature, float rawTemperature)
    {
        if (filteredTemperature <= 0f)
            return 0f;

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_smoothedCpuTemperature <= 0f || rawTemperature >= 95f)
        {
            _smoothedCpuTemperature = rawTemperature >= 95f ? rawTemperature : filteredTemperature;
            _lastCpuTemperatureTimestamp = now;
            return _smoothedCpuTemperature;
        }

        var elapsedSeconds = _lastCpuTemperatureTimestamp == 0
            ? 1d
            : Math.Clamp(System.Diagnostics.Stopwatch.GetElapsedTime(_lastCpuTemperatureTimestamp, now).TotalSeconds, 0.1d, 5d);
        var timeConstantSeconds = filteredTemperature >= _smoothedCpuTemperature ? 2d : 3.5d;
        var alpha = (float)(1d - Math.Exp(-elapsedSeconds / timeConstantSeconds));
        _smoothedCpuTemperature += (filteredTemperature - _smoothedCpuTemperature) * alpha;
        _lastCpuTemperatureTimestamp = now;
        return _smoothedCpuTemperature;
    }

    public List<GpuStatModel> GetGpusStats()
    {
        lock (_syncLock)
        {
            var result = new List<GpuStatModel>();
            
            foreach (var gpu in _gpus)
            {
                var stat = new GpuStatModel
                {
                    Id = gpu.Identifier.ToString(),
                    Name = gpu.Name,
                    VramTotal = 8f // Default fallback
                };

                var sensors = EnumerateSensors(gpu).ToList();
                var tempSensor = FindTemperatureSensor(gpu, "GPU Core", "Core", "GPU", "Edge");
                var loadSensor = sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                 ?? sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
                
                var dedicatedVramSensor = sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Dedicated Memory Used", StringComparison.OrdinalIgnoreCase));
                var sharedVramSensor = sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase));
                
                var vramSensor = dedicatedVramSensor ?? sharedVramSensor;
                
                if (vramSensor != null)
                {
                    stat.IsVramAvailable = true;
                    stat.IsSharedMemory = dedicatedVramSensor == null;
                    stat.VramUsed = vramSensor.Value.GetValueOrDefault() / 1024f; // MB to GB
                }

                var vramTotalSensor = sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase));
                if (vramTotalSensor?.Value != null) 
                {
                    stat.VramTotal = vramTotalSensor.Value.Value / 1024f; // MB to GB
                }
                else if (stat.IsSharedMemory && _ram != null)
                {
                    // Fallback for iGPU: VRAM total might just be half of RAM or dynamic. Just keep it available but maybe not accurate total.
                    stat.VramTotal = GetRamStats().total; // Just a rough estimate if missing
                }
                
                stat.Temperature = ReadTemperature(tempSensor);
                if (loadSensor?.Value != null) stat.Usage = loadSensor.Value.Value;
                
                result.Add(stat);
            }
            
            return result;
        }
    }

    private static IEnumerable<ISensor> EnumerateSensors(IHardware hardware)
    {
        foreach (var sensor in hardware.Sensors)
            yield return sensor;

        foreach (var subHardware in hardware.SubHardware)
        {
            foreach (var sensor in EnumerateSensors(subHardware))
                yield return sensor;
        }
    }

    private static ISensor? FindTemperatureSensor(IHardware hardware, params string[] preferredNames)
    {
        var sensors = EnumerateSensors(hardware)
            .Where(sensor => sensor.SensorType == SensorType.Temperature &&
                             !sensor.Name.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase) &&
                             ReadTemperature(sensor) > 0f)
            .ToList();

        foreach (var preferredName in preferredNames)
        {
            var preferred = sensors.FirstOrDefault(sensor =>
                sensor.Name.Contains(preferredName, StringComparison.OrdinalIgnoreCase));
            if (preferred != null)
                return preferred;
        }

        return sensors.FirstOrDefault();
    }

    private static float ReadTemperature(ISensor? sensor)
    {
        if (sensor?.Value is float value && float.IsFinite(value) && value > 0f && value <= 150f)
            return value;

        return 0f;
    }

    private float ReadDellCpuTemperature()
    {
        var now = DateTime.UtcNow;
        if (now < _nextDellCpuTemperatureReadUtc)
            return _cachedDellCpuTemperature;

        // Dell Command Monitor exposes BIOS-backed temperatures through WMI on supported systems.
        // Cache the lookup so a missing provider is not queried on every telemetry tick.
        _nextDellCpuTemperatureReadUtc = now.AddSeconds(5);
        _cachedDellCpuTemperature = 0f;

        try
        {
            var scope = new ManagementScope(@"\\.\root\dcim\sysman");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT * FROM DCIM_NumericSensor WHERE SensorType = 2"));
            using var sensors = searcher.Get();

            foreach (ManagementObject sensor in sensors)
            {
                var name = string.Join(" ", new[]
                {
                    Convert.ToString(sensor["ElementName"], CultureInfo.InvariantCulture),
                    Convert.ToString(sensor["Name"], CultureInfo.InvariantCulture),
                    Convert.ToString(sensor["Description"], CultureInfo.InvariantCulture)
                });

                if (!name.Contains("CPU", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Processor", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (sensor["CurrentReading"] is not IConvertible readingValue)
                    continue;

                var reading = readingValue.ToDouble(CultureInfo.InvariantCulture);
                var unit = Convert.ToInt32(sensor["BaseUnits"], CultureInfo.InvariantCulture);
                var modifier = sensor["UnitModifier"] is IConvertible modifierValue
                    ? modifierValue.ToInt32(CultureInfo.InvariantCulture)
                    : 0;

                reading *= Math.Pow(10, modifier);
                reading = unit switch
                {
                    2 => reading,                         // Degrees Celsius
                    3 => (reading - 32d) * 5d / 9d,      // Degrees Fahrenheit
                    4 => reading - 273.15d,               // Kelvin
                    _ => double.NaN
                };

                if (double.IsFinite(reading) && reading > 0d && reading <= 150d)
                {
                    _cachedDellCpuTemperature = (float)reading;
                    break;
                }
            }
        }
        catch
        {
            // The WMI provider is optional; keep the LHM result unavailable if it is absent.
        }

        return _cachedDellCpuTemperature;
    }

    public (float used, float total) GetRamStats()
    {
        try
        {
            var mem = new PCHealthDashboard.Helpers.NativeMethods.MEMORYSTATUSEX();
            if (PCHealthDashboard.Helpers.NativeMethods.GlobalMemoryStatusEx(mem) && mem.ullTotalPhys > 0)
            {
                float total = (float)(mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0));
                float used = (float)((mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0));
                return (used, total);
            }
        }
        catch { }

        lock (_syncLock)
        {
            float fallbackUsed = 0f, fallbackTotal = 16f;
            if (_ram != null)
            {
                var usedSensor = _ram.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Memory Used"));
                var availSensor = _ram.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Memory Available"));
                
                if (usedSensor?.Value != null && availSensor?.Value != null)
                {
                    fallbackUsed = usedSensor.Value.Value;
                    fallbackTotal = fallbackUsed + availSensor.Value.Value;
                }
            }
            return (fallbackUsed, fallbackTotal);
        }
    }

    public (float read, float write, float usedSpace, float totalSpace) GetStorageStats()
    {
        float read = 0f, write = 0f, usedSpace = 0f, totalSpace = 0f;
        
        lock (_syncLock)
        {
            foreach (var hw in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Storage))
            {
                var readSensor = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Read Rate"));
                var writeSensor = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Write Rate"));
                if (readSensor?.Value != null) read += readSensor.Value.Value;
                if (writeSensor?.Value != null) write += writeSensor.Value.Value;
            }
        }
        
        // Use DriveInfo for accurate space
        try
        {
            string systemRoot = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new System.IO.DriveInfo(systemRoot);
            if (drive.IsReady && drive.DriveType == System.IO.DriveType.Fixed)
            {
                totalSpace = drive.TotalSize / (1024f * 1024f * 1024f); // GB
                usedSpace = totalSpace - (drive.AvailableFreeSpace / (1024f * 1024f * 1024f));
            }
        }
        catch { }

        return (read, write, usedSpace, totalSpace);
    }

    public (float download, float upload) GetNetworkStats()
    {
        float down = 0f, up = 0f;
        lock (_syncLock)
        {
            var activeNetworks = _computer.Hardware.Where(h => h.HardwareType == HardwareType.Network);
            foreach (var net in activeNetworks)
            {
                var dlSensor = net.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Throughput && s.Name.Contains("Download"));
                var ulSensor = net.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Throughput && s.Name.Contains("Upload"));
                
                if (dlSensor?.Value != null) down += dlSensor.Value.Value;
                if (ulSensor?.Value != null) up += ulSensor.Value.Value;
            }
        }
        
        // Convert Bytes/s to Mbps
        return (down * 8 / 1048576f, up * 8 / 1048576f);
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            try { _computer.Close(); } catch { }
        }
    }
}

