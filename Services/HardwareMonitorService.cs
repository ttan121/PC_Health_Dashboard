using LibreHardwareMonitor.Hardware;
using PCHealthDashboard.Models;
using System;
using System.Collections.Generic;
using System.Linq;

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
            try
            {
                _cpu?.Update();
                _ram?.Update();
                foreach (var gpu in _gpus)
                {
                    gpu.Update();
                }

                // Only full update storage/network every 3 ticks to save CPU
                if (fullUpdate || _updateCounter % 3 == 0)
                {
                    foreach (var hw in _computer.Hardware)
                    {
                        if (hw.HardwareType == HardwareType.Storage || hw.HardwareType == HardwareType.Network)
                        {
                            hw.Update();
                        }
                    }
                }
            }
            catch { }
        }
    }

    public (float usage, float temp, float power, float clock) GetCpuStats()
    {
        lock (_syncLock)
        {
            float load = 0f, temp = 0f, power = 0f, clock = 0f;
            if (_cpu != null)
            {
                var loadSensor = _cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Total"));
                var tempSensor = _cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Package"))
                              ?? _cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Core Max"))
                              ?? _cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Core Average"))
                              ?? _cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                var powerSensor = _cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("Package"));
                var clockSensor = _cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock);

                if (loadSensor?.Value != null) load = loadSensor.Value.Value;
                if (tempSensor?.Value != null) temp = tempSensor.Value.Value;
                if (powerSensor?.Value != null) power = powerSensor.Value.Value;
                if (clockSensor?.Value != null) clock = clockSensor.Value.Value;

                if (temp == 0f)
                {
                    temp = 45f; // User requested fallback to 45 if IC is locked/unreadable
                }
            }
            return (load, temp, power, clock);
        }
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

                var tempSensor = gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                var loadSensor = gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Core"))
                                 ?? gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
                
                var dedicatedVramSensor = gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Dedicated Memory Used"));
                var sharedVramSensor = gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Used"));
                
                var vramSensor = dedicatedVramSensor ?? sharedVramSensor;
                
                if (vramSensor != null)
                {
                    stat.IsVramAvailable = true;
                    stat.IsSharedMemory = dedicatedVramSensor == null;
                    stat.VramUsed = vramSensor.Value.GetValueOrDefault() / 1024f; // MB to GB
                }

                var vramTotalSensor = gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Total"));
                if (vramTotalSensor?.Value != null) 
                {
                    stat.VramTotal = vramTotalSensor.Value.Value / 1024f; // MB to GB
                }
                else if (stat.IsSharedMemory && _ram != null)
                {
                    // Fallback for iGPU: VRAM total might just be half of RAM or dynamic. Just keep it available but maybe not accurate total.
                    stat.VramTotal = GetRamStats().total; // Just a rough estimate if missing
                }
                
                if (tempSensor?.Value != null) stat.Temperature = tempSensor.Value.Value;
                if (loadSensor?.Value != null) stat.Usage = loadSensor.Value.Value;
                
                result.Add(stat);
            }
            
            return result;
        }
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
                var loadSensor = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Used Space"));
                
                if (readSensor?.Value != null) read += readSensor.Value.Value;
                if (writeSensor?.Value != null) write += writeSensor.Value.Value;
            }
        }
        
        // Use DriveInfo for accurate space
        try
        {
            var drive = System.IO.DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == System.IO.DriveType.Fixed);
            if (drive != null)
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

