// ============================================================================
// PC Health Dashboard - Models/MetricPoint.cs
// Zero-Allocation Time-Series Telemetry Metric Struct
// ============================================================================

using System;

namespace PCHealthDashboard.Models;

/// <summary>
/// Immutable value-type telemetry point representing a scalar metric at a specific point in time.
/// Used in zero-allocation circular buffers (RingBuffer) for continuous sparkline rendering.
/// </summary>
public readonly record struct MetricPoint(long TimestampUtcTicks, float Value)
{
    /// <summary>
    /// Constructs a MetricPoint with current UTC timestamp.
    /// </summary>
    public MetricPoint(float value) : this(DateTime.UtcNow.Ticks, value)
    {
    }

    /// <summary>
    /// Gets the timestamp converted to UTC DateTime.
    /// </summary>
    public DateTime UtcDateTime => new(TimestampUtcTicks, DateTimeKind.Utc);

    /// <summary>
    /// Empty/uninitialized metric point.
    /// </summary>
    public static readonly MetricPoint Empty = new(0L, 0f);
}
