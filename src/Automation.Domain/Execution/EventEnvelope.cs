using System.Text.Json.Nodes;

namespace Automation.Domain.Execution;

public sealed record EventEnvelope(
    Guid Id,
    string Provider,
    string ConnectionId,
    string ProviderEventId,
    string EventType,
    int EventVersion,
    DateTimeOffset ReceivedAt,
    JsonObject Data);
