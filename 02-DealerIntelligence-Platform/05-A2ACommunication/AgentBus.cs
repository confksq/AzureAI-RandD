// ============================================================
// MODULE 08: A2A Protocol — Agent Message Bus
// ============================================================
// WHAT: Central message routing layer between agents
//       Handles: send, receive, validate schema, log audit trail
// WHY:  Agents shouldn't call each other directly (tight coupling)
//       Bus = loose coupling, centralized logging, schema enforcement
// JMA:  All inter-agent messages route through AgentBus
// HEALTHCARE EQUIVALENT: Same pattern — clinical agents communicate
//       via message bus, not direct calls, for HIPAA audit compliance
// ============================================================

using System.Text.Json;

namespace DealerIntelligence.A2ACommunication;

public class AgentBus
{
    private readonly ILogger<AgentBus> _logger;
    private readonly TelemetryClient   _telemetry;
    private readonly IMessageValidator _validator;

    public AgentBus(
        ILogger<AgentBus> logger,
        TelemetryClient   telemetry,
        IMessageValidator validator)
    {
        _logger    = logger;
        _telemetry = telemetry;
        _validator = validator;
    }

    /// <summary>
    /// Sends a typed message from one agent to another.
    /// Validates schema, logs to audit trail, routes to receiver.
    /// INTERVIEW: Every message in a clinical multi-agent system must be logged.
    /// You need to prove: who sent what to whom, when, and with what data.
    /// </summary>
    public async Task SendAsync<TPayload>(
        AgentMessage<TPayload> message,
        Func<AgentMessage<TPayload>, Task> handler)
    {
        // INTERVIEW: Schema validation before routing
        // Rejects malformed messages before they corrupt downstream agents
        var validation = _validator.Validate(message);
        if (!validation.IsValid)
        {
            _logger.LogError(
                "[A2A] Schema validation failed | MsgId: {MsgId} | Errors: {Errors}",
                message.MessageId, string.Join(", ", validation.Errors));
            throw new MessageValidationException(message.MessageId, validation.Errors);
        }

        // INTERVIEW: Audit log BEFORE delivery — not after
        // If delivery fails, you still have the record that it was attempted
        LogMessage("SENT", message);

        try
        {
            await handler(message);
            LogMessage("DELIVERED", message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[A2A] Message delivery failed | MsgId: {MsgId} | From: {Sender} → To: {Receiver}",
                message.MessageId, message.SenderId, message.ReceiverId);

            // INTERVIEW: Dead letter — message that couldn't be delivered
            // In production: route to dead letter queue → alert on-call → human investigates
            await DeadLetterAsync(message, ex.Message);
            throw;
        }
    }

    private void LogMessage<TPayload>(string action, AgentMessage<TPayload> message)
    {
        // INTERVIEW: Structured log — every field queryable in App Insights
        _logger.LogInformation(
            "[A2A] {Action} | MsgId: {MsgId} | CorrelationId: {CorrId} | " +
            "From: {Sender} → To: {Receiver} | Type: {Type} | Schema: {Schema}",
            action,
            message.MessageId,
            message.CorrelationId,
            message.SenderId,
            message.ReceiverId,
            message.MessageType,
            message.SchemaVersion);

        // Track in App Insights for dashboards
        _telemetry.TrackEvent($"AgentMessage_{action}", new Dictionary<string, string>
        {
            ["message_id"]     = message.MessageId,
            ["correlation_id"] = message.CorrelationId,
            ["sender"]         = message.SenderId,
            ["receiver"]       = message.ReceiverId,
            ["message_type"]   = message.MessageType
        });
    }

    private async Task DeadLetterAsync<TPayload>(AgentMessage<TPayload> message, string error)
    {
        // Route to Azure Service Bus dead letter queue
        // In production: ops team investigates undelivered agent messages
        _logger.LogCritical(
            "[A2A DEAD LETTER] MsgId: {MsgId} | Error: {Error} | Payload: {Payload}",
            message.MessageId,
            error,
            JsonSerializer.Serialize(message.Payload));

        await Task.CompletedTask; // production: ServiceBusClient.SendMessageAsync(...)
    }
}

public record ValidationResult(bool IsValid, string[] Errors);
public interface IMessageValidator
{
    ValidationResult Validate<T>(AgentMessage<T> message);
}

public class MessageValidationException(string messageId, string[] errors)
    : Exception($"Message {messageId} failed schema validation: {string.Join(", ", errors)}");
