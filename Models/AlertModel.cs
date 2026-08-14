namespace PCHealthDashboard.Models
{
    public enum AlertSeverity
    {
        Info,
        Warning,
        Critical
    }

    public class AlertModel
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Metric { get; set; } = "";
        public string Recommendation { get; set; } = "";
        public AlertSeverity Severity { get; set; } = AlertSeverity.Info;
    }
}
