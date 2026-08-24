// ============================================================================
// PC Health Dashboard - Services/IHealthScoreCalculator.cs
// Interface contract for Asymmetric EWMA Health Scoring Engine
// ============================================================================

using PCHealthDashboard.Models;

namespace PCHealthDashboard.Services;

/// <summary>
/// Service contract for evaluating PC health telemetry and generating smoothed health scores.
/// </summary>
public interface IHealthScoreCalculator
{
    /// <summary>
    /// Gets the current rounded integer health score [0 - 100].
    /// </summary>
    int CurrentScore { get; }

    /// <summary>
    /// Gets the current precise floating-point smoothed health score.
    /// </summary>
    float SmoothedScore { get; }

    /// <summary>
    /// Evaluates a hardware telemetry snapshot and returns a detailed health assessment.
    /// </summary>
    /// <param name="snapshot">Hardware snapshot passed by readonly reference.</param>
    /// <returns>HealthEvaluationResult containing overall score, status band, sub-scores, and alerts.</returns>
    HealthEvaluationResult Evaluate(in HardwareSnapshot snapshot);

    /// <summary>
    /// Resets the internal state and smoothed score to default baseline.
    /// </summary>
    void Reset();
}
