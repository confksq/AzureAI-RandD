// ============================================================
// MODULE 08: A2A Protocol — Typed Agent-to-Agent Messages
// ============================================================
// WHAT: Defines the schema for messages passed between agents
//       A2A = Agent-to-Agent communication protocol
// WHY:  Agents need to communicate structured results to each other
//       Typed schema = validation, versioning, audit logging
//       Untyped messages = debugging nightmare in production
// JMA:  ClaimValidatorAgent sends typed result to SupervisorAgent
// HEALTHCARE EQUIVALENT: ClinicalCriteriaAgent sends typed result
//       to SupervisorAgent with structured clinical findings
// ============================================================
// INTERVIEW: "What is A2A Protocol?"
// "A2A is a standard for how AI agents communicate with each other —
//  typed message schemas, versioned envelopes, schema validation,
//  and audit logging on every message. In healthcare this matters
//  because you need to prove exactly what information flowed between
//  agents for a clinical decision. An untyped 'pass a string' approach
//  breaks down fast in multi-agent production systems."
// ============================================================

namespace DealerIntelligence.A2ACommunication;

/// <summary>
/// Typed envelope for all agent-to-agent messages.
/// INTERVIEW: Envelope pattern = metadata wrapping the payload
/// Same pattern as HTTP headers wrapping the body
/// </summary>
public record AgentMessage<TPayload>
{
    // INTERVIEW: MessageId = unique ID for every message — enables dedup and tracing
    public string    MessageId    { get; init; } = Guid.NewGuid().ToString();

    // INTERVIEW: CorrelationId = the original claim/request ID that started the workflow
    // Threads all messages for one claim together in logs
    public string    CorrelationId { get; init; } = string.Empty;

    // INTERVIEW: Schema versioning — if you change the message format,
    // old and new agents can coexist during rolling deployments
    public string    SchemaVersion { get; init; } = "1.0";

    public string    SenderId      { get; init; } = string.Empty;
    public string    ReceiverId    { get; init; } = string.Empty;
    public string    MessageType   { get; init; } = string.Empty;
    public DateTime  SentAt        { get; init; } = DateTime.UtcNow;

    // The actual typed payload — different per message type
    public TPayload  Payload       { get; init; } = default!;
}

/// <summary>
/// Message sent from ClaimValidatorAgent to SupervisorAgent
/// HEALTHCARE EQUIVALENT: EligibilityResult sent to SupervisorAgent
/// </summary>
public record ValidationResultPayload
{
    public string   ClaimId       { get; init; } = string.Empty;
    public bool     IsValid       { get; init; }
    public string   FailureReason { get; init; } = string.Empty;
    public string[] MissingFields { get; init; } = [];

    // INTERVIEW: Confidence score on the validation — not just pass/fail
    // Allows supervisor to make nuanced decisions
    public double   Confidence    { get; init; } = 1.0;
}

/// <summary>
/// Message sent from PolicyCheckerAgent to SupervisorAgent
/// HEALTHCARE EQUIVALENT: FormularyCheckResult sent to SupervisorAgent
/// </summary>
public record PolicyCheckPayload
{
    public string   ClaimId       { get; init; } = string.Empty;
    public bool     MeetsCriteria { get; init; }
    public string   DenialReason  { get; init; } = string.Empty;
    public string   PolicyRef     { get; init; } = string.Empty;

    // INTERVIEW: Evidence = the retrieved chunks that support the decision
    // Supervisor can include these in the final decision rationale
    public string[] EvidenceChunks { get; init; } = [];
}

/// <summary>
/// Message sent from FraudDetectorAgent to SupervisorAgent
/// HEALTHCARE EQUIVALENT: AnomalyDetectionResult
/// </summary>
public record FraudCheckPayload
{
    public string   ClaimId    { get; init; } = string.Empty;
    public double   RiskScore  { get; init; }
    public string[] Indicators { get; init; } = [];
    public bool     RequiresHumanReview { get; init; }
}

/// <summary>
/// Message types enum — prevents magic strings in routing logic
/// INTERVIEW: Typed routing = no "if messageType == 'validation_result'" strings
/// </summary>
public enum AgentMessageType
{
    ValidationRequest,
    ValidationResult,
    PolicyCheckRequest,
    PolicyCheckResult,
    FraudCheckRequest,
    FraudCheckResult,
    SupervisorDecision,
    EscalationRequest
}
