using System;
using System.Globalization;

namespace PCHealthDashboard.Helpers;

public static class ByteSizeFormatter
{
    /// <summary>
    /// Formats a size in megabytes.
    /// If >= 1024 MB, formats as "X MB (Y GB)", e.g. "1024 MB (1.00 GB)".
    /// Otherwise, formats as "X MB", e.g. "500 MB".
    /// </summary>
    public static string FormatMb(double totalMb)
    {
        if (double.IsNaN(totalMb) || double.IsInfinity(totalMb) || totalMb <= 0) return "0 MB";

        if (Math.Round(totalMb) >= 1024.0)
        {
            double gb = totalMb / 1024.0;
            return $"{Math.Round(totalMb).ToString("F0", CultureInfo.InvariantCulture)} MB ({gb.ToString("F2", CultureInfo.InvariantCulture)} GB)";
        }

        if (totalMb < 1.0)
        {
            return $"{totalMb.ToString("F2", CultureInfo.InvariantCulture)} MB";
        }

        return $"{Math.Round(totalMb).ToString("F0", CultureInfo.InvariantCulture)} MB";
    }

    /// <summary>
    /// Formats a size in raw bytes into MB/GB representation.
    /// </summary>
    public static string FormatBytes(long totalBytes)
    {
        if (totalBytes <= 0) return "0 MB";
        double totalMb = totalBytes / 1024.0 / 1024.0;
        return FormatMb(totalMb);
    }
}
