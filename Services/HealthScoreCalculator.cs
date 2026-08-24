// ============================================================================
// PC Health Dashboard - Services/HealthScoreCalculator.cs
// High-Performance Asymmetric EWMA Health Scoring Engine
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using PCHealthDashboard.Models;

namespace PCHealthDashboard.Services;

/// <summary>
/// Implements the Asymmetric Exponentially Weighted Moving Average (EWMA) health scoring model.
/// S_t = α * M_t + (1 - α) * S_{t-1}
/// Uses α_drop = 0.35 for fast degradation response and α_recover = 0.08 for stable recovery.
/// </summary>
public sealed class HealthScoreCalculator : IHealthScoreCalculator
{
    // EWMA Smoothing Factors
    public const float DefaultAlphaDrop = 0.35f;
    public const float DefaultAlphaRecover = 0.08f;

    // Component Weights (Sum = 1.0)
    public const float WeightThermal = 0.30f;
    public const float WeightLoad = 0.20f;
    public const float WeightRam = 0.25f;
    public const float WeightStorage = 0.15f;
    public const float WeightNetwork = 0.10f;

    private readonly float _alphaDrop;
    private readonly float _alphaRecover;
    private readonly Lock _lock = new();

    private float _smoothedScore = 100.0f;
    private bool _isInitialized = false;

    /// <summary>
    /// Initializes a new instance of HealthScoreCalculator with default asymmetric EWMA coefficients.
    /// </summary>
    public HealthScoreCalculator(float alphaDrop = DefaultAlphaDrop, float alphaRecover = DefaultAlphaRecover)
    {
        if (alphaDrop <= 0f || alphaDrop > 1f)
            throw new ArgumentOutOfRangeException(nameof(alphaDrop), "Alpha drop must be in range (0, 1].");
        if (alphaRecover <= 0f || alphaRecover > 1f)
            throw new ArgumentOutOfRangeException(nameof(alphaRecover), "Alpha recover must be in range (0, 1].");

        _alphaDrop = alphaDrop;
        _alphaRecover = alphaRecover;
        _smoothedScore = 100.0f;
        _isInitialized = false;
    }

    /// <inheritdoc />
    public int CurrentScore
    {
        get
        {
            lock (_lock)
            {
                return (int)Math.Clamp(Math.Round(_smoothedScore), 0, 100);
            }
        }
    }

    /// <inheritdoc />
    public float SmoothedScore
    {
        get
        {
            lock (_lock)
            {
                return _smoothedScore;
            }
        }
    }

    /// <inheritdoc />
    public HealthEvaluationResult Evaluate(in HardwareSnapshot snapshot)
    {
        var alerts = new List<string>(4);

        // 1. Calculate Sub-Scores & Detect Threat Alerts
        float thermalScore = CalculateThermalScore(snapshot, alerts);
        float loadScore = CalculateLoadScore(snapshot, alerts);
        float ramScore = CalculateRamScore(snapshot, alerts);
        float storageScore = CalculateStorageScore(snapshot, alerts);
        float networkScore = CalculateNetworkScore(snapshot, alerts);

        // 2. Compute Instantaneous Composite Metric M_t
        float instantScore = (thermalScore * WeightThermal) +
                             (loadScore * WeightLoad) +
                             (ramScore * WeightRam) +
                             (storageScore * WeightStorage) +
                             (networkScore * WeightNetwork);

        instantScore = Math.Clamp(instantScore, 0f, 100f);

        // 3. Asymmetric EWMA Smoothing
        float smoothed;
        lock (_lock)
        {
            if (!_isInitialized)
            {
                _smoothedScore = instantScore;
                _isInitialized = true;
            }
            else
            {
                float alpha = (instantScore < _smoothedScore) ? _alphaDrop : _alphaRecover;
                _smoothedScore = (alpha * instantScore) + ((1.0f - alpha) * _smoothedScore);
                _smoothedScore = Math.Clamp(_smoothedScore, 0f, 100f);
            }

            smoothed = _smoothedScore;
        }

        int finalIntScore = (int)Math.Clamp(Math.Round(smoothed), 0, 100);
        string statusBand = HealthStatusBands.FromScore(finalIntScore);

        return new HealthEvaluationResult(
            Score: finalIntScore,
            StatusBand: statusBand,
            ThermalScore: thermalScore,
            LoadScore: loadScore,
            RamScore: ramScore,
            StorageScore: storageScore,
            NetworkScore: networkScore,
            ActiveAlerts: alerts
        );
    }

    /// <inheritdoc />
    public void Reset()
    {
        lock (_lock)
        {
            _smoothedScore = 100.0f;
            _isInitialized = false;
        }
    }

    // =========================================================================
    // Sub-Score Computation Methods
    // =========================================================================

    private static float CalculateThermalScore(in HardwareSnapshot snapshot, List<string> alerts)
    {
        float maxGpuTemp = (snapshot.GpuCount > 0) ? snapshot.GpuTemp : 0f;
        float maxTemp = Math.Max(snapshot.CpuTemp, maxGpuTemp);

        // Thermal Alerts
        if (snapshot.CpuTemp > 85f)
        {
            alerts.Add($"High CPU Temperature\nCPU is running at {snapshot.CpuTemp:F0}°C. Thermal throttling or inadequate cooling detected.");
        }
        else if (snapshot.CpuTemp > 75f)
        {
            alerts.Add($"Elevated CPU Temperature\nCPU is running warm at {snapshot.CpuTemp:F0}°C.");
        }

        if (snapshot.GpuCount > 0)
        {
            if (snapshot.GpuTemp > 85f)
            {
                alerts.Add($"High GPU Temperature\nGPU is running at {snapshot.GpuTemp:F0}°C. Check airflow or fan curves.");
            }
            else if (snapshot.GpuTemp > 80f)
            {
                alerts.Add($"Elevated GPU Temperature\nGPU is running warm at {snapshot.GpuTemp:F0}°C.");
            }
        }

        // Piecewise Thermal Health Curve
        if (maxTemp <= 65f)
            return 100f;

        if (maxTemp <= 80f)
            return 100f - ((maxTemp - 65f) * (20f / 15f)); // 100 -> 80

        if (maxTemp <= 90f)
            return 80f - ((maxTemp - 80f) * 4.0f); // 80 -> 40

        return Math.Max(0f, 40f - ((maxTemp - 90f) * 8.0f)); // 40 -> 0 (critical)
    }

    private static float CalculateLoadScore(in HardwareSnapshot snapshot, List<string> alerts)
    {
        float maxGpuUsage = (snapshot.GpuCount > 0) ? snapshot.GpuUsage : 0f;
        float maxLoad = Math.Max(snapshot.CpuUsage, maxGpuUsage);

        if (maxLoad > 95f)
        {
            alerts.Add($"High System Load\nHardware utilization reached {maxLoad:F0}%. System resources are near saturation.");
        }

        if (maxLoad <= 75f)
            return 100f;

        if (maxLoad <= 95f)
            return 100f - ((maxLoad - 75f) * 1.5f); // 100 -> 70

        return Math.Max(30f, 70f - ((maxLoad - 95f) * 8.0f)); // 70 -> 30
    }

    private static float CalculateRamScore(in HardwareSnapshot snapshot, List<string> alerts)
    {
        if (snapshot.RamTotalGb <= 0f)
            return 100f;

        float ramPct = (snapshot.RamUsedGb / snapshot.RamTotalGb) * 100f;

        if (ramPct > 90f)
        {
            alerts.Add($"High Memory Pressure\nMemory usage is at {ramPct:F0}% ({snapshot.RamUsedGb:F1}/{snapshot.RamTotalGb:F1} GB). Deep RAM cleanup recommended.");
        }

        if (ramPct <= 70f)
            return 100f;

        if (ramPct <= 85f)
            return 100f - ((ramPct - 70f) * (20f / 15f)); // 100 -> 80

        if (ramPct <= 95f)
            return 80f - ((ramPct - 85f) * 4.0f); // 80 -> 40

        return Math.Max(0f, 40f - ((ramPct - 95f) * 8.0f)); // 40 -> 0
    }

    private static float CalculateStorageScore(in HardwareSnapshot snapshot, List<string> alerts)
    {
        if (snapshot.SsdTotalGb <= 0f)
            return 100f;

        float freeGb = Math.Max(0f, snapshot.SsdTotalGb - snapshot.SsdUsedGb);
        float freePct = (freeGb / snapshot.SsdTotalGb) * 100f;

        // Space Sub-Score
        float spaceScore;
        if (freePct >= 20f)
            spaceScore = 100f;
        else if (freePct >= 10f)
            spaceScore = 70f + ((freePct - 10f) * 3.0f); // 70 -> 100
        else
            spaceScore = Math.Max(0f, freePct * 7.0f); // 0 -> 70

        if (freeGb < 10f || freePct < 10f)
        {
            alerts.Add($"Low Disk Space\nLess than {freeGb:F1} GB ({freePct:F0}%) free on primary drive. Junk file cleanup recommended.");
        }

        // SMART Health Sub-Score
        float smartScore = (snapshot.SsdHealth > 0f) ? Math.Clamp(snapshot.SsdHealth, 0f, 100f) : 100f;
        if (snapshot.SsdHealth > 0f && snapshot.SsdHealth < 70f)
        {
            alerts.Add($"Storage Wear Warning\nDrive SMART health is at {snapshot.SsdHealth:F0}%. Consider backup.");
        }

        return (spaceScore * 0.5f) + (smartScore * 0.5f);
    }

    private static float CalculateNetworkScore(in HardwareSnapshot snapshot, List<string> alerts)
    {
        // Stable baseline network health
        return 100f;
    }
}
