using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TelemetryPipeline.Models;

namespace TelemetryPipeline.Functions;

public sealed class QueueEventProcessor(ILogger<QueueEventProcessor> logger)
{
    private static readonly SemaphoreSlim FileWriteLock = new(1, 1);

    [Function(nameof(QueueEventProcessor))]
    public async Task Run(
        [QueueTrigger("telemetry-queue", Connection = "AzureWebJobsStorage")]
        string queueMessage)
    {
        EventPayload? eventPayload;

        try
        {
            eventPayload = JsonConvert.DeserializeObject<EventPayload>(queueMessage);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Queue message is not valid telemetry JSON.");
            throw;
        }

        if (eventPayload is null
            || string.IsNullOrWhiteSpace(eventPayload.EventId)
            || string.IsNullOrWhiteSpace(eventPayload.SourceSystem)
            || string.IsNullOrWhiteSpace(eventPayload.EventType)
            || eventPayload.PayloadData is null
            || eventPayload.Timestamp == default)
        {
            logger.LogError("Queue message is missing required telemetry fields.");
            throw new InvalidDataException("Queue message is missing required telemetry fields.");
        }

        ProcessedEvent processedEvent = new()
        {
            EventId = eventPayload.EventId,
            SourceSystem = eventPayload.SourceSystem,
            EventType = eventPayload.EventType,
            PayloadData = eventPayload.PayloadData,
            Timestamp = eventPayload.Timestamp,
            LogId = Guid.NewGuid(),
            ProcessedAtUtc = DateTimeOffset.UtcNow
        };

        string databasePath = Path.Combine(Environment.CurrentDirectory, "local-database.json");
        string jsonLine = JsonConvert.SerializeObject(processedEvent) + Environment.NewLine;

        await FileWriteLock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(databasePath, jsonLine);
        }
        finally
        {
            FileWriteLock.Release();
        }

        logger.LogInformation(
            "Processed event {EventId} as log record {LogId} and appended it to {DatabasePath}.",
            processedEvent.EventId,
            processedEvent.LogId,
            databasePath);
    }
}
