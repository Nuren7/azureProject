using Newtonsoft.Json.Linq;

namespace TelemetryPipeline.Models;

public class EventPayload
{
    public string? EventId { get; set; }

    public string? SourceSystem { get; set; }

    public string? EventType { get; set; }

    public JToken? PayloadData { get; set; }

    public DateTimeOffset Timestamp { get; set; }
}
