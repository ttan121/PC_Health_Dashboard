namespace PCHealthDashboard.Models;

/// <summary>
/// Read-only physical-drive data reported by the Windows Storage provider.
/// WearPercent is the provider's estimated endurance already consumed, not a failure probability.
/// </summary>
public sealed record StorageDriveReading(
    string DeviceId,
    string FriendlyName,
    int? MediaTypeId,
    int? BusTypeId,
    int? HealthStatusId,
    int? TemperatureCelsius,
    int? WearPercent,
    int? AvailableSparePercent,
    int? SpareThresholdPercent,
    byte? NvmeCriticalWarning,
    ulong? PowerOnHours,
    ulong? UnsafeShutdowns,
    ulong? MediaErrors,
    ulong? ReadErrorsUncorrected,
    ulong? WriteErrorsUncorrected,
    string ReliabilitySource)
{
    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName)
        ? $"Physical drive {DeviceId}"
        : FriendlyName;

    public string DeviceIdDisplay => $"Disk {DeviceId}";
    public string DriveTypeDisplay => $"{MediaTypeDisplay} · {BusTypeDisplay}";

    public string MediaTypeDisplay => MediaTypeId switch
    {
        3 => "HDD",
        4 => "SSD",
        5 => "SCM",
        _ => "Loại ổ chưa rõ"
    };

    public bool IsSsd => MediaTypeId == 4 || BusTypeId == 17;

    public string BusTypeDisplay => BusTypeId switch
    {
        1 => "SCSI",
        3 => "ATA",
        7 => "USB",
        8 => "RAID",
        11 => "SATA",
        17 => "NVMe",
        null => "Bus chưa rõ",
        _ => $"Bus {BusTypeId}"
    };

    public string DeviceHealthDisplay => HealthStatusId switch
    {
        0 => "Windows báo: Tốt",
        1 => "Windows báo: Cảnh báo",
        2 => "Windows báo: Không khỏe",
        5 => "Windows báo: Không xác định",
        null => "Windows: N/A",
        _ => $"Windows: trạng thái {HealthStatusId}"
    };

    public string CriticalWarningDisplay
    {
        get
        {
            if (NvmeCriticalWarning is not byte warning)
                return string.Empty;
            if (warning == 0)
                return "NVMe critical warning: none";

            var reasons = new List<string>(6);
            if ((warning & 0x01) != 0) reasons.Add("spare khả dụng thấp hơn ngưỡng");
            if ((warning & 0x02) != 0) reasons.Add("nhiệt độ vượt ngưỡng");
            if ((warning & 0x04) != 0) reasons.Add("độ tin cậy subsystem bị suy giảm");
            if ((warning & 0x08) != 0) reasons.Add("ổ chuyển sang chỉ đọc");
            if ((warning & 0x10) != 0) reasons.Add("bộ nhớ dự phòng mất điện bị lỗi");
            if ((warning & 0x20) != 0) reasons.Add("vùng persistent memory chuyển sang chỉ đọc");
            if ((warning & 0xC0) != 0) reasons.Add("có cờ cảnh báo khác");

            return $"NVMe critical warning 0x{warning:X2}: {string.Join(", ", reasons)}";
        }
    }

    public string WearDisplay => WearPercent is int wear
        ? $"Wear ước tính: {wear}% đã dùng · còn khoảng {Math.Max(0, 100 - wear)}% endurance"
        : "Wear ước tính: Windows/driver chưa cung cấp";

    public string TemperatureDisplay => TemperatureCelsius is int temperature
        ? $"Nhiệt độ: {temperature}°C"
        : "Nhiệt độ: N/A";

    public string AvailableSpareDisplay => AvailableSparePercent is int spare
        ? $"Spare khả dụng: {spare}%" + (SpareThresholdPercent is int threshold ? $" (ngưỡng {threshold}%)" : string.Empty)
        : string.Empty;

    public string PowerOnHoursDisplay => PowerOnHours is ulong hours
        ? $"Đã chạy: {hours:N0} giờ"
        : "Giờ hoạt động: N/A";

    public string NvmeErrorCountersDisplay => MediaErrors.HasValue || UnsafeShutdowns.HasValue
        ? $"NVMe media errors: {MediaErrors?.ToString("N0") ?? "N/A"} · tắt máy bất thường: {UnsafeShutdowns?.ToString("N0") ?? "N/A"}"
        : string.Empty;

    public string ReliabilitySourceDisplay => $"Nguồn dữ liệu: {ReliabilitySource}";

    public string UncorrectedErrorsDisplay => ReadErrorsUncorrected.HasValue || WriteErrorsUncorrected.HasValue
        ? $"Lỗi chưa sửa: đọc {ReadErrorsUncorrected?.ToString("N0") ?? "N/A"}, ghi {WriteErrorsUncorrected?.ToString("N0") ?? "N/A"}"
        : "Lỗi chưa sửa: N/A";
}

/// <summary>
/// A point-in-time list of physical drives and the provider status for reliability counters.
/// </summary>
public sealed record StorageHealthSnapshot(
    IReadOnlyList<StorageDriveReading> Drives,
    string Status)
{
    public static StorageHealthSnapshot Empty { get; } = new(
        Array.Empty<StorageDriveReading>(),
        "Đang chờ dữ liệu ổ đĩa.");
}
