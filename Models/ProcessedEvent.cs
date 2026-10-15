namespace TelemetryPipeline.Models;

public sealed class ProcessedEvent : EventPayload
{
    public Guid LogId { get; set; }

    public DateTimeOffset ProcessedAtUtc { get; set; }

    public string SchemaVersion { get; set; } = "1.0";
}
