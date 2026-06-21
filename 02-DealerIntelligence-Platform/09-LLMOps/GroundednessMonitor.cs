// ============================================================
// GAP TOPIC: LLMOps — Groundedness Monitoring in Production
// ============================================================
// WHAT: Continuously evaluates agent responses in production
//       Checks: is the response grounded in retrieved chunks?
//       Alerts when groundedness drops below threshold
// WHY:  Eval pipeline runs before deploy (preventive)
//       Groundedness monitor runs in production (detective)
//       Together they form the full LLMOps quality loop
// JMA:  Every claim decision is scored for groundedness in prod
//       If score drops → alert on-call → investigate prompt drift
// HEALTHCARE EQUIVALENT: Every prior auth decision scored live
//       Groundedness drops → clinical safety alert → immediate investigation
//       In healthcare, groundedness monitoring is a patient safety feature
// ============================================================
// INTERVIEW: "How do you monitor AI quality in production?"
// "Two layers: evaluation before deploy (quality gate) and
//  groundedness monitoring in production (continuous).
//  Every agent response gets scored by GPT-4o: is the decision
//  supported by the retrieved policy chunks? We track rolling
//  average groundedness in App Insights. If it drops below 0.80,
//  we alert on-call. Common causes: policy documents changed,
//  chunking quality degraded, or the model drifted.
//  In healthcare, we'd alert a clinical quality officer too."
// ============================================================

namespace DealerIntelligence.LLMOps;

public class GroundednessMonitor
{
    private readonly EvaluatorLLM      _evaluator;
    private readonly TelemetryClient   _telemetry;
    private readonly ILogger<GroundednessMonitor> _logger;

    // INTERVIEW: Production thresholds — tighter than CI because this is live
    private const double AlertThreshold  = 0.80;
    private const double CriticalThreshold = 0.70;  // page on-call immediately

    public GroundednessMonitor(
        EvaluatorLLM evaluator,
        TelemetryClient telemetry,
        ILogger<GroundednessMonitor> logger)
    {
        _evaluator = evaluator;
        _telemetry = telemetry;
        _logger    = logger;
    }

    /// <summary>
    /// Scores a single agent response for groundedness.
    /// Called for every production claim decision — runs asynchronously (not on critical path).
    /// INTERVIEW: Run asynchronously so it doesn't block the claim response to the dealer.
    /// </summary>
    public async Task MonitorAsync(ProductionEvalInput input)
    {
        var score = await _evaluator.ScoreAsync(new EvalInput
        {
            Question      = input.ClaimDescription,
            Context       = input.RetrievedPolicyChunks,
            AgentResponse = input.AgentRationale,
            AgentDecision = input.Decision
        });

        // INTERVIEW: Structured telemetry — every field queryable in App Insights dashboard
        _telemetry.TrackMetric("claim.groundedness", score.Groundedness);
        _telemetry.TrackMetric("claim.relevance",    score.Relevance);
        _telemetry.TrackEvent("ClaimDecisionScored", new Dictionary<string, string>
        {
            ["claim_id"]     = input.ClaimId,
            ["decision"]     = input.Decision,
            ["groundedness"] = score.Groundedness.ToString("F3"),
            ["relevance"]    = score.Relevance.ToString("F3")
        });

        // INTERVIEW: Alert escalation based on severity
        if (score.Groundedness < CriticalThreshold)
        {
            _logger.LogCritical(
                "[GROUNDEDNESS CRITICAL] Claim {ClaimId}: groundedness={Score:F2} — " +
                "BELOW CRITICAL THRESHOLD. Page on-call. Decision may not be grounded in policy.",
                input.ClaimId, score.Groundedness);
            // Production: PagerDuty alert, Teams critical channel, clinical officer notification
        }
        else if (score.Groundedness < AlertThreshold)
        {
            _logger.LogWarning(
                "[GROUNDEDNESS WARNING] Claim {ClaimId}: groundedness={Score:F2} — " +
                "Below target threshold. Investigating prompt or document quality drift.",
                input.ClaimId, score.Groundedness);
        }
        else
        {
            _logger.LogDebug(
                "[GROUNDEDNESS OK] Claim {ClaimId}: groundedness={Score:F2}",
                input.ClaimId, score.Groundedness);
        }
    }
}

public record ProductionEvalInput
{
    public string ClaimId              { get; init; } = string.Empty;
    public string ClaimDescription     { get; init; } = string.Empty;
    public string RetrievedPolicyChunks { get; init; } = string.Empty;
    public string AgentRationale       { get; init; } = string.Empty;
    public string Decision             { get; init; } = string.Empty;
}
