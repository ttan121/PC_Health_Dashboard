using System;

namespace PCHealthDashboard.Models;

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public class AlertModel
{
    public string Id { get; set; } = string.Empty; // Unique identifier for debouncing
    public AlertSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
