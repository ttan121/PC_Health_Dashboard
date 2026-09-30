using PCHealthDashboard.Models;
using System.Globalization;
using System.Management;

namespace PCHealthDashboard.Services;

/// <summary>
/// Reads physical-drive reliability counters from the Windows Storage provider.
/// Calls are cached because the dashboard polls other telemetry once per second.
/// </summary>
public sealed class StorageHealthService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);
    private readonly object _sync = new();
    private DateTime _lastReadUtc = DateTime.MinValue;
    private StorageHealthSnapshot _cached = StorageHealthSnapshot.Empty;

    public StorageHealthSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            if (DateTime.UtcNow - _lastReadUtc < RefreshInterval)
                return _cached;

            _cached = ReadSnapshot();
            _lastReadUtc = DateTime.UtcNow;
            return _cached;
        }
    }

    private static StorageHealthSnapshot ReadSnapshot()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();

            var disks = new List<DiskProperties>();
            using (var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT DeviceId, FriendlyName, MediaType, BusType, HealthStatus FROM MSFT_PhysicalDisk"),
                CreateEnumerationOptions()))
            using (var results = searcher.Get())
            {
                foreach (ManagementBaseObject item in results)
                {
                    using (item)
                    {
                        disks.Add(new DiskProperties(
                            ReadString(item, "DeviceId") ?? "?",
                            ReadString(item, "FriendlyName") ?? string.Empty,
                            ReadNullableInt(item, "MediaType"),
                            ReadNullableInt(item, "BusType"),
                            ReadNullableInt(item, "HealthStatus")));
                    }
                }
            }

            if (disks.Count == 0)
            {
                return new StorageHealthSnapshot(
                    Array.Empty<StorageDriveReading>(),
                    "Windows không liệt kê ổ vật lý qua Storage provider.");
            }

            var counters = new Dictionary<string, CounterProperties>(StringComparer.OrdinalIgnoreCase);
            string? counterStatus = null;
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    scope,
                    new ObjectQuery("SELECT DeviceId, Temperature, Wear, PowerOnHours, ReadErrorsUncorrected, WriteErrorsUncorrected FROM MSFT_StorageReliabilityCounter"),
                    CreateEnumerationOptions());
                using var results = searcher.Get();
                foreach (ManagementBaseObject item in results)
                {
                    using (item)
                    {
                        string? deviceId = ReadString(item, "DeviceId");
                        if (string.IsNullOrWhiteSpace(deviceId))
                            continue;

                        counters[deviceId] = new CounterProperties(
                            ReadNullableInt(item, "Temperature"),
                            ReadNullableInt(item, "Wear"),
                            ReadNullableUlong(item, "PowerOnHours"),
                            ReadNullableUlong(item, "ReadErrorsUncorrected"),
                            ReadNullableUlong(item, "WriteErrorsUncorrected"));
                    }
                }
            }
            catch (ManagementException ex)
            {
                counterStatus = GetProviderFailureMessage(ex);
            }
            catch
            {
                counterStatus = "Không đọc được reliability counters; Windows/driver có thể không hỗ trợ các trường SMART này.";
            }

            var readings = disks
                .Select(disk =>
                {
                    counters.TryGetValue(disk.DeviceId, out CounterProperties? counter);
                    NvmeSmartReading? nvme = disk.BusTypeId == 17
                        ? NvmeSmartReader.TryRead(disk.DeviceId)
                        : null;
                    return new StorageDriveReading(
                        disk.DeviceId,
                        disk.FriendlyName,
                        disk.MediaTypeId,
                        disk.BusTypeId,
                        disk.HealthStatusId,
                        nvme?.TemperatureCelsius ?? counter?.TemperatureCelsius,
                        nvme?.PercentageUsed ?? counter?.WearPercent,
                        nvme?.AvailableSparePercent,
                        nvme?.SpareThresholdPercent,
                        nvme?.CriticalWarning,
                        nvme?.PowerOnHours ?? counter?.PowerOnHours,
                        nvme?.UnsafeShutdowns,
                        nvme?.MediaErrors,
                        counter?.ReadErrorsUncorrected,
                        counter?.WriteErrorsUncorrected,
                        nvme is not null ? "NVMe SMART log (Windows IOCTL)" :
                        counter is not null ? "Windows Storage reliability counters" : "N/A");
                })
                .ToArray();

            bool hasReliabilityData = readings.Any(reading =>
                reading.TemperatureCelsius.HasValue || reading.WearPercent.HasValue ||
                reading.PowerOnHours.HasValue || reading.ReadErrorsUncorrected.HasValue ||
                reading.WriteErrorsUncorrected.HasValue || reading.AvailableSparePercent.HasValue ||
                reading.NvmeCriticalWarning.HasValue || reading.MediaErrors.HasValue);

            string status = hasReliabilityData
                ? "Số liệu do Windows/firmware báo cáo; wear là ước tính endurance, không phải ngày ổ sẽ hỏng."
                : counterStatus ?? "Windows/driver chưa cung cấp SMART, wear hoặc bộ đếm độ tin cậy cho các ổ này.";

            return new StorageHealthSnapshot(readings, status);
        }
        catch (ManagementException ex)
        {
            return new StorageHealthSnapshot(Array.Empty<StorageDriveReading>(), GetProviderFailureMessage(ex));
        }
        catch (UnauthorizedAccessException)
        {
            return new StorageHealthSnapshot(
                Array.Empty<StorageDriveReading>(),
                "Không có quyền đọc thông tin ổ đĩa từ Windows Storage provider.");
        }
        catch
        {
            return new StorageHealthSnapshot(
                Array.Empty<StorageDriveReading>(),
                "Không đọc được thông tin ổ đĩa từ Windows Storage provider.");
        }
    }

    private static EnumerationOptions CreateEnumerationOptions() => new()
    {
        Timeout = QueryTimeout,
        ReturnImmediately = false,
        Rewindable = false
    };

    private static string GetProviderFailureMessage(ManagementException exception) =>
        exception.ErrorCode == ManagementStatus.AccessDenied
            ? "Windows từ chối quyền truy cập Storage provider."
            : "Storage provider hoặc driver không hỗ trợ đọc bộ đếm SMART/reliability trên ổ này.";

    private static string? ReadString(ManagementBaseObject item, string property) =>
        Convert.ToString(item[property], CultureInfo.InvariantCulture);

    private static int? ReadNullableInt(ManagementBaseObject item, string property)
    {
        object? value = item[property];
        if (value is null)
            return null;

        try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static ulong? ReadNullableUlong(ManagementBaseObject item, string property)
    {
        object? value = item[property];
        if (value is null)
            return null;

        try { return Convert.ToUInt64(value, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private sealed record DiskProperties(
        string DeviceId,
        string FriendlyName,
        int? MediaTypeId,
        int? BusTypeId,
        int? HealthStatusId);

    private sealed record CounterProperties(
        int? TemperatureCelsius,
        int? WearPercent,
        ulong? PowerOnHours,
        ulong? ReadErrorsUncorrected,
        ulong? WriteErrorsUncorrected);
}
