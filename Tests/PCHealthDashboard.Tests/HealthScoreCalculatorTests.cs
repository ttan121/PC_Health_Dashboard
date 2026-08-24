// ============================================================================
// PC Health Dashboard - Tests/PCHealthDashboard.Tests/HealthScoreCalculatorTests.cs
// Unit Tests with Synthetic Hardware Data Injection for Asymmetric EWMA Engine
// ============================================================================

using System;
using System.Collections.Generic;
using PCHealthDashboard.Models;
using PCHealthDashboard.Services;
using Xunit;

namespace PCHealthDashboard.Tests;

public class HealthScoreCalculatorTests
{
    private static HardwareSnapshot CreateIdealSnapshot() => new(
        TimestampUtcTicks: DateTime.UtcNow.Ticks,
        CpuUsage: 15f,
        CpuTemp: 45f,
        CpuPower: 35f,
        CpuClock: 3800f,
        RamUsedGb: 6f,
        RamTotalGb: 32f,
        SsdUsedGb: 200f,
        SsdTotalGb: 1000f,
        SsdHealth: 100f,
        NetDownMbps: 25f,
        NetUpMbps: 10f,
        GpuCount: 1,
        GpuUsage: 20f,
        GpuTemp: 48f,
        GpuVramUsedGb: 2f,
        GpuVramTotalGb: 12f
    );

    [Fact]
    public void TEST_HS_01_IdealBaselineCondition_ProducesScore100_AndHealthyStatus()
    {
        // Arrange
        var calculator = new HealthScoreCalculator();
        var snapshot = CreateIdealSnapshot();

        // Act
        var result = calculator.Evaluate(in snapshot);

        // Assert
        Assert.Equal(100, result.Score);
        Assert.Equal(100f, result.ThermalScore);
        Assert.Equal(100f, result.LoadScore);
        Assert.Equal(100f, result.RamScore);
        Assert.Equal(100f, result.StorageScore);
        Assert.Equal(100f, result.NetworkScore);
        Assert.Equal(HealthStatusBands.Healthy, result.StatusBand);
        Assert.Equal(HealthStatusBands.ColorHealthy, result.StatusColor);
        Assert.Empty(result.ActiveAlerts);
    }

    [Fact]
    public void TEST_HS_02_SingleCycleThermalSpike_IsDampenedByEWMA()
    {
        // Arrange
        var calculator = new HealthScoreCalculator();
        var ideal = CreateIdealSnapshot();

        // Establish 10 cycles of baseline health (Score = 100)
        for (int i = 0; i < 10; i++)
        {
            calculator.Evaluate(in ideal);
        }
        Assert.Equal(100, calculator.CurrentScore);

        // Act: Inject a single transient extreme thermal spike (CPU 95°C, thermal subscore 0)
        var spikeSnapshot = ideal with { CpuTemp = 95f };
        var spikeResult = calculator.Evaluate(in spikeSnapshot);

        // Assert:
        // Instantaneous score dropped (Thermal = 0 -> 100 - (0.30 * 100) = 70)
        // With alpha_drop = 0.35: S_t = 0.35 * 70 + 0.65 * 100 = 24.5 + 65.0 = 89.5 -> rounded = 90.
        // EWMA prevents catastrophic false-positive plunging to Critical (<60).
        Assert.Equal(0f, spikeResult.ThermalScore);
        Assert.True(spikeResult.Score >= 88, $"Expected score to remain dampened >= 88, but got {spikeResult.Score}");
        Assert.True(spikeResult.Score < 100, "Expected score to reflect the thermal threat");
        Assert.Contains(spikeResult.ActiveAlerts, a => a.Contains("High CPU Temperature"));
    }

    [Fact]
    public void TEST_HS_03_SustainedThermalStress_DecaysToCriticalBand()
    {
        // Arrange
        var calculator = new HealthScoreCalculator();
        var ideal = CreateIdealSnapshot();

        // Establish baseline
        calculator.Evaluate(in ideal);
        Assert.Equal(100, calculator.CurrentScore);

        // Act: Sustained 15 cycles of severe overheating (CPU 94°C, GPU 92°C)
        var hotSnapshot = ideal with { CpuTemp = 94f, GpuTemp = 92f };
        int previousScore = 100;
        HealthEvaluationResult finalResult = default;

        for (int cycle = 1; cycle <= 15; cycle++)
        {
            finalResult = calculator.Evaluate(in hotSnapshot);
            Assert.True(finalResult.Score <= previousScore, $"Cycle {cycle}: Score must not increase under sustained heat");
            previousScore = finalResult.Score;
        }

        // Assert: Under sustained heat, score must decay into Warning / Critical band
        Assert.True(finalResult.Score < 75, $"Expected decayed score < 75, got {finalResult.Score}");
        Assert.Contains(finalResult.ActiveAlerts, a => a.Contains("High CPU Temperature"));
        Assert.Contains(finalResult.ActiveAlerts, a => a.Contains("High GPU Temperature"));
    }

    [Fact]
    public void TEST_HS_04_AsymmetricRecovery_IsSlowerThanDegradation()
    {
        // Arrange
        var calculator = new HealthScoreCalculator(alphaDrop: 0.35f, alphaRecover: 0.08f);
        var ideal = CreateIdealSnapshot();
        calculator.Evaluate(in ideal);

        // Degrade system with severe load (Instant score drops to ~40)
        var severeLoad = ideal with
        {
            CpuTemp = 92f,
            CpuUsage = 98f,
            GpuTemp = 90f,
            RamUsedGb = 31f,
            RamTotalGb = 32f
        };

        // 10 cycles of degradation
        for (int i = 0; i < 10; i++)
        {
            calculator.Evaluate(in severeLoad);
        }

        float degradedScore = calculator.SmoothedScore;
        Assert.True(degradedScore < 60f, $"Expected degraded score < 60, got {degradedScore}");

        // Act: 1 cycle of recovery back to ideal conditions
        var recoveryResult1 = calculator.Evaluate(in ideal);
        float recoveryDelta1 = recoveryResult1.Score - degradedScore;

        // Reset and test 1 cycle of degradation from ideal with the same difference
        calculator.Reset();
        calculator.Evaluate(in ideal); // starts at 100
        float startIdeal = calculator.SmoothedScore; // 100
        var dropResult1 = calculator.Evaluate(in severeLoad);
        float dropDelta1 = startIdeal - calculator.SmoothedScore;

        // Assert:
        // Recovery step must be significantly smaller than drop step due to asymmetric alpha
        // Drop rate (0.35) vs Recovery rate (0.08)
        Assert.True(dropDelta1 > recoveryDelta1 * 2.5f,
            $"Drop delta ({dropDelta1:F2}) should be > 2.5x recovery delta ({recoveryDelta1:F2}) due to alpha asymmetry.");
    }

    [Fact]
    public void TEST_HS_05_ExtremeMultiComponentDegradation_TriggersMultipleAlerts()
    {
        // Arrange
        var calculator = new HealthScoreCalculator();
        var extremeSnapshot = new HardwareSnapshot(
            TimestampUtcTicks: DateTime.UtcNow.Ticks,
            CpuUsage: 99f,
            CpuTemp: 98f,
            CpuPower: 120f,
            CpuClock: 4500f,
            RamUsedGb: 32f,
            RamTotalGb: 32f,      // 100% RAM used
            SsdUsedGb: 995f,
            SsdTotalGb: 1000f,    // 5GB free (0.5% free)
            SsdHealth: 35f,       // 35% SMART health
            NetDownMbps: 0f,
            NetUpMbps: 0f,
            GpuCount: 1,
            GpuUsage: 99f,
            GpuTemp: 94f,
            GpuVramUsedGb: 11.9f,
            GpuVramTotalGb: 12f
        );

        // Act: Evaluate single cycle
        var result = calculator.Evaluate(in extremeSnapshot);

        // Assert:
        // All sub-scores must be heavily degraded
        Assert.Equal(0f, result.ThermalScore);
        Assert.True(result.LoadScore <= 40f, $"Expected LoadScore <= 40, got {result.LoadScore}");
        Assert.Equal(0f, result.RamScore);
        Assert.True(result.StorageScore <= 25f, $"Expected StorageScore <= 25, got {result.StorageScore}");

        // Alerts must contain CPU, GPU, RAM, Disk, and SMART alerts
        Assert.True(result.ActiveAlerts.Count >= 4, $"Expected >= 4 alerts, got {result.ActiveAlerts.Count}");
        Assert.Contains(result.ActiveAlerts, a => a.Contains("CPU"));
        Assert.Contains(result.ActiveAlerts, a => a.Contains("GPU"));
        Assert.Contains(result.ActiveAlerts, a => a.Contains("Memory"));
        Assert.Contains(result.ActiveAlerts, a => a.Contains("Disk Space") || a.Contains("Storage"));
    }

    [Theory]
    [InlineData(100, HealthStatusBands.Healthy, HealthStatusBands.ColorHealthy)]
    [InlineData(90, HealthStatusBands.Healthy, HealthStatusBands.ColorHealthy)]
    [InlineData(89, HealthStatusBands.Good, HealthStatusBands.ColorGood)]
    [InlineData(75, HealthStatusBands.Good, HealthStatusBands.ColorGood)]
    [InlineData(74, HealthStatusBands.Warning, HealthStatusBands.ColorWarning)]
    [InlineData(60, HealthStatusBands.Warning, HealthStatusBands.ColorWarning)]
    [InlineData(59, HealthStatusBands.Critical, HealthStatusBands.ColorCritical)]
    [InlineData(0, HealthStatusBands.Critical, HealthStatusBands.ColorCritical)]
    public void TEST_HS_06_StatusBand_Boundaries_MatchSpecification(int score, string expectedBand, string expectedColor)
    {
        // Act
        string band = HealthStatusBands.FromScore(score);
        string color = HealthStatusBands.ColorFromScore(score);

        // Assert
        Assert.Equal(expectedBand, band);
        Assert.Equal(expectedColor, color);
    }

    [Fact]
    public void TEST_HS_07_WeightsSumToOne()
    {
        // Act
        float totalWeight = HealthScoreCalculator.WeightThermal +
                            HealthScoreCalculator.WeightLoad +
                            HealthScoreCalculator.WeightRam +
                            HealthScoreCalculator.WeightStorage +
                            HealthScoreCalculator.WeightNetwork;

        // Assert
        Assert.Equal(1.0f, totalWeight, 4);
    }

    [Fact]
    public void TEST_HS_08_Reset_RestoresBaselineState()
    {
        // Arrange
        var calculator = new HealthScoreCalculator();
        var extremeHot = CreateIdealSnapshot() with { CpuTemp = 98f };

        // Act: Run degraded evaluations
        for (int i = 0; i < 10; i++)
        {
            calculator.Evaluate(in extremeHot);
        }
        Assert.True(calculator.SmoothedScore < 80f);

        // Reset
        calculator.Reset();

        // Assert
        Assert.Equal(100f, calculator.SmoothedScore);
        Assert.Equal(100, calculator.CurrentScore);

        // Re-evaluate ideal snapshot
        var ideal = CreateIdealSnapshot();
        var freshResult = calculator.Evaluate(in ideal);
        Assert.Equal(100, freshResult.Score);
        Assert.Equal(HealthStatusBands.Healthy, freshResult.StatusBand);
    }

    [Fact]
    public void TEST_HS_09_NoGpuSnapshot_ComputesSafely()
    {
        // Arrange
        var calculator = new HealthScoreCalculator();
        var snapshot = CreateIdealSnapshot() with
        {
            GpuCount = 0,
            GpuUsage = 0f,
            GpuTemp = 0f,
            GpuVramUsedGb = 0f,
            GpuVramTotalGb = 0f
        };

        // Act
        var result = calculator.Evaluate(in snapshot);

        // Assert
        Assert.Equal(100, result.Score);
        Assert.Equal(100f, result.ThermalScore);
        Assert.Equal(100f, result.LoadScore);
        Assert.Empty(result.ActiveAlerts);
    }

    [Fact]
    public void TEST_HS_10_BoundaryAndEdgeValues_NeverProduceNaNOrInfinity()
    {
        // Arrange
        var calculator = new HealthScoreCalculator();
        var extremeEdge = new HardwareSnapshot(
            TimestampUtcTicks: 0L,
            CpuUsage: float.MaxValue,
            CpuTemp: float.MaxValue,
            CpuPower: float.MaxValue,
            CpuClock: float.MaxValue,
            RamUsedGb: float.MaxValue,
            RamTotalGb: 0f,
            SsdUsedGb: float.MaxValue,
            SsdTotalGb: 0f,
            SsdHealth: -100f,
            NetDownMbps: float.MaxValue,
            NetUpMbps: float.MaxValue,
            GpuCount: 0,
            GpuUsage: 0f,
            GpuTemp: 0f,
            GpuVramUsedGb: 0f,
            GpuVramTotalGb: 0f
        );

        // Act
        var result = calculator.Evaluate(in extremeEdge);

        // Assert
        Assert.False(float.IsNaN(result.Score));
        Assert.False(float.IsInfinity(result.Score));
        Assert.InRange(result.Score, 0, 100);
        Assert.False(float.IsNaN(calculator.SmoothedScore));
    }
}
