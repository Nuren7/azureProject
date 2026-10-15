using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TelemetryPipeline.Models;

namespace TelemetryPipeline.Functions;

public sealed class TelemetryIngestApi(ILogger<TelemetryIngestApi> logger)
{
    [Function(nameof(TelemetryIngestApi))]
    public async Task<TelemetryIngestOutput> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/v1/telemetry")]
        HttpRequestData request)
    {
        string body = await request.ReadAsStringAsync() ?? string.Empty;
        EventPayload? eventPayload;

        try
        {
            eventPayload = JsonConvert.DeserializeObject<EventPayload>(body);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Rejected telemetry request because its JSON body is invalid.");
            return await CreateErrorOutput(request, HttpStatusCode.BadRequest, "Request body must be valid JSON.");
        }

        if (eventPayload is null || !IsValid(eventPayload))
        {
            logger.LogWarning("Rejected telemetry request because one or more required fields are missing or invalid.");
            return await CreateErrorOutput(
                request,
                HttpStatusCode.BadRequest,
                "EventId, SourceSystem, EventType, PayloadData, and a valid Timestamp are required.");
        }

        string queueMessage = JsonConvert.SerializeObject(eventPayload);
        HttpResponseData response = request.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new { eventPayload.EventId });

        logger.LogInformation(
            "Accepted telemetry event {EventId} from {SourceSystem} with type {EventType}.",
            eventPayload.EventId,
            eventPayload.SourceSystem,
            eventPayload.EventType);

        return new TelemetryIngestOutput
        {
            QueueMessage = queueMessage,
            HttpResponse = response
        };
    }

    private static bool IsValid(EventPayload? eventPayload)
    {
        return eventPayload is not null
            && !string.IsNullOrWhiteSpace(eventPayload.EventId)
            && !string.IsNullOrWhiteSpace(eventPayload.SourceSystem)
            && !string.IsNullOrWhiteSpace(eventPayload.EventType)
            && eventPayload.PayloadData is not null
            && eventPayload.Timestamp != default;
    }

    private static async Task<TelemetryIngestOutput> CreateErrorOutput(
        HttpRequestData request,
        HttpStatusCode statusCode,
        string message)
    {
        HttpResponseData response = request.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new { Error = message });
        return new TelemetryIngestOutput { HttpResponse = response };
    }
}

public sealed class TelemetryIngestOutput
{
    [QueueOutput("telemetry-queue", Connection = "AzureWebJobsStorage")]
    public string? QueueMessage { get; init; }

    public required HttpResponseData HttpResponse { get; init; }
}
