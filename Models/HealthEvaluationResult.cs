// ============================================================================
// PC Health Dashboard - Models/HealthEvaluationResult.cs
// Health Evaluation Result Struct conforming to Master Project Specification
// ============================================================================

using System;
using System.Collections.Generic;

namespace PCHealthDashboard.Models;

/// <summary>
/// Status bands for the PC Health Score.
/// </summary>
public static class HealthStatusBands
{
    public const string Healthy = "Healthy";
    public const string Good = "Good";
    public const string Warning = "Warning";
    public const string Critical = "Critical";

    public const string ColorHealthy = "#10b981";  // Emerald Green (90-100)
    public const string ColorGood = "#3b82f6";     // Blue (75-89)
    public const string ColorWarning = "#f59e0b";  // Amber (60-74)
    public const string ColorCritical = "#ef4444"; // Coral Red (<60)

    /// <summary>
    /// Returns the semantic status band for a given score (0-100).
    /// </summary>
    public static string FromScore(int score) => score switch
    {
        >= 90 => Healthy,
        >= 75 => Good,
        >= 60 => Warning,
        _ => Critical
    };

    /// <summary>
    /// Returns the hex color string for a given score (0-100).
    /// </summary>
    public static string ColorFromScore(int score) => score switch
    {
        >= 90 => ColorHealthy,
        >= 75 => ColorGood,
        >= 60 => ColorWarning,
        _ => ColorCritical
    };
}

/// <summary>
/// Immutable snapshot result of an Asymmetric EWMA Health Score evaluation.
/// Contains the overall smoothed score, semantic status band, sub-scores, and active alerts.
/// </summary>
public readonly record struct HealthEvaluationResult(
    int Score,
    string StatusBand,
    float ThermalScore,
    float LoadScore,
    float RamScore,
    float StorageScore,
    float NetworkScore,
    IReadOnlyList<string> ActiveAlerts
)
{
    /// <summary>
    /// Hex color string corresponding to the current health status band.
    /// </summary>
    public string StatusColor => HealthStatusBands.ColorFromScore(Score);

    /// <summary>
    /// Returns an empty / default evaluation result.
    /// </summary>
    public static readonly HealthEvaluationResult Empty = new(
        Score: 100,
        StatusBand: HealthStatusBands.Healthy,
        ThermalScore: 100f,
        LoadScore: 100f,
        RamScore: 100f,
        StorageScore: 100f,
        NetworkScore: 100f,
        ActiveAlerts: Array.Empty<string>()
    );
}
