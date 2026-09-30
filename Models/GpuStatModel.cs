using CommunityToolkit.Mvvm.ComponentModel;

namespace PCHealthDashboard.Models;

public partial class GpuStatModel : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private float _temperature;
    [ObservableProperty] private float _usage;
    [ObservableProperty] private float _vramUsed;
    [ObservableProperty] private float _vramTotal;
    
    // Distinguish between dedicated and shared memory
    [ObservableProperty] private bool _isSharedMemory;
    
    // Whether VRAM telemetry is available at all
    [ObservableProperty] private bool _isVramAvailable;

    public string TemperatureDisplay => float.IsFinite(Temperature) && Temperature > 0f ? $"{Temperature:F0}°C" : "N/A";

    partial void OnTemperatureChanged(float value)
    {
        OnPropertyChanged(nameof(TemperatureDisplay));
    }
}
