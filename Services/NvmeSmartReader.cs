using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;

namespace PCHealthDashboard.Services;

/// <summary>
/// Reads the standard NVMe SMART / Health Information log through the documented,
/// read-only Windows IOCTL_STORAGE_QUERY_PROPERTY protocol-specific query.
/// </summary>
internal static class NvmeSmartReader
{
    private const uint StorageDeviceProtocolSpecificProperty = 50;
    private const uint PropertyStandardQuery = 0;
    private const uint ProtocolTypeNvme = 3;
    private const uint NvmeDataTypeLogPage = 2;
    private const uint NvmeSmartHealthLogPage = 2;
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const int PropertyQueryHeaderSize = 8;
    private const int ProtocolSpecificDataSize = 40;
    private const int NvmeHealthLogSize = 512;
    private const int DescriptorHeaderSize = PropertyQueryHeaderSize + ProtocolSpecificDataSize;

    public static NvmeSmartReading? TryRead(string deviceId)
    {
        try
        {
            return TryReadCore(deviceId);
        }
        catch
        {
            // A denied or unsupported physical-drive query must not break other telemetry.
            return null;
        }
    }

    private static NvmeSmartReading? TryReadCore(string deviceId)
    {
        if (!int.TryParse(deviceId, NumberStyles.None, CultureInfo.InvariantCulture, out int diskNumber) || diskNumber < 0)
            return null;

        using SafeFileHandle handle = CreateFile(
            $"\\\\.\\PhysicalDrive{diskNumber}",
            0, // No file data access; IOCTL_STORAGE_QUERY_PROPERTY is a read-only query.
            0x00000003, // FILE_SHARE_READ | FILE_SHARE_WRITE
            IntPtr.Zero,
            3, // OPEN_EXISTING
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
            return null;

        byte[] buffer = new byte[DescriptorHeaderSize + NvmeHealthLogSize];
        WriteUInt32(buffer, 0, StorageDeviceProtocolSpecificProperty);
        WriteUInt32(buffer, 4, PropertyStandardQuery);
        WriteUInt32(buffer, 8, ProtocolTypeNvme);
        WriteUInt32(buffer, 12, NvmeDataTypeLogPage);
        WriteUInt32(buffer, 16, NvmeSmartHealthLogPage);
        WriteUInt32(buffer, 20, 0);
        WriteUInt32(buffer, 24, ProtocolSpecificDataSize);
        WriteUInt32(buffer, 28, NvmeHealthLogSize);

        if (!DeviceIoControl(
                handle,
                IoctlStorageQueryProperty,
                buffer,
                (uint)buffer.Length,
                buffer,
                (uint)buffer.Length,
                out uint bytesReturned,
                IntPtr.Zero) ||
            bytesReturned < DescriptorHeaderSize)
        {
            return null;
        }

        uint descriptorVersion = ReadUInt32(buffer, 0);
        uint descriptorSize = ReadUInt32(buffer, 4);
        uint protocolDataOffset = ReadUInt32(buffer, 24);
        uint protocolDataLength = ReadUInt32(buffer, 28);

        if (descriptorVersion < DescriptorHeaderSize ||
            descriptorSize < DescriptorHeaderSize ||
            protocolDataOffset < ProtocolSpecificDataSize ||
            protocolDataLength < NvmeHealthLogSize)
        {
            return null;
        }

        int logStart = PropertyQueryHeaderSize + checked((int)protocolDataOffset);
        if (logStart < 0 || logStart + NvmeHealthLogSize > bytesReturned)
            return null;

        byte criticalWarning = buffer[logStart];
        ushort kelvin = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(logStart + 1, 2));
        int? temperatureCelsius = kelvin is > 273 and < 700 ? kelvin - 273 : null;
        int? availableSpare = buffer[logStart + 3] <= 100 ? buffer[logStart + 3] : null;
        int? spareThreshold = buffer[logStart + 4] <= 100 ? buffer[logStart + 4] : null;
        int percentageUsed = buffer[logStart + 5];
        ulong powerOnHours = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(logStart + 128, 8));
        ulong unsafeShutdowns = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(logStart + 144, 8));
        ulong mediaErrors = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(logStart + 160, 8));

        return new NvmeSmartReading(
            criticalWarning,
            temperatureCelsius,
            availableSpare,
            spareThreshold,
            percentageUsed,
            powerOnHours,
            unsafeShutdowns,
            mediaErrors);
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, sizeof(uint)), value);

    private static uint ReadUInt32(byte[] buffer, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, sizeof(uint)));

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        byte[] inputBuffer,
        uint inputBufferSize,
        byte[] outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);
}

internal sealed record NvmeSmartReading(
    byte CriticalWarning,
    int? TemperatureCelsius,
    int? AvailableSparePercent,
    int? SpareThresholdPercent,
    int PercentageUsed,
    ulong PowerOnHours,
    ulong UnsafeShutdowns,
    ulong MediaErrors);
