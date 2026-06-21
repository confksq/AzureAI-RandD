// ============================================================
// MODULE 07: Meta-Agent Hierarchies — Supervisor Agent
// ============================================================
// WHAT: The Supervisor Agent receives the claim, breaks it into
//       sub-tasks, delegates each to a specialist sub-agent,
//       collects results, and synthesizes the final decision
// WHY:  Complex tasks need specialization — one agent can't do
//       deep validation + policy checking + fraud detection well
//       Specialization = better accuracy, easier to debug
// JMA:  Supervisor delegates: ClaimValidator + PolicyChecker + FraudDetector
// HEALTHCARE EQUIVALENT: Supervisor delegates to EligibilityAgent +
//       FormularyAgent + ClinicalCriteriaAgent + PharmacyBenefitAgent
// ============================================================
// INTERVIEW: "What is a meta-agent hierarchy?"
// "A supervisor agent receives a complex task and breaks it into
//  sub-tasks. Each sub-task goes to a specialist agent — one focused
//  on validation, one on policy, one on fraud. The supervisor collects
//  all results and makes the final decision. This is better than one
//  monolithic agent because each specialist has a focused system prompt,
//  focused tools, and focused knowledge — it's more accurate, easier
//  to debug, and you can update one specialist without touching others."
// ============================================================

using Microsoft.SemanticKernel;

namespace DealerIntelligence.MetaAgentOrchestration;

public class SupervisorAgent
{
    private readonly ClaimValidatorAgent  _validator;
    private readonly PolicyCheckerAgent   _policyChecker;
    private readonly FraudDetectorAgent   _fraudDetector;
    private readonly ILogger<SupervisorAgent> _logger;

    public SupervisorAgent(
        ClaimValidatorAgent  validator,
        PolicyCheckerAgent   policyChecker,
        FraudDetectorAgent   fraudDetector,
        ILogger<SupervisorAgent> logger)
    {
        _validator     = validator;
        _policyChecker = policyChecker;
        _fraudDetector = fraudDetector;
        _logger        = logger;
    }

    /// <summary>
    /// Orchestrates the full multi-agent claim review pipeline.
    /// INTERVIEW: This is the supervisor pattern — delegate, collect, synthesize.
    /// </summary>
    public async Task<SupervisorDecision> OrchestrateAsync(ClaimRequest request)
    {
        _logger.LogInformation("[SUPERVISOR] Starting orchestration for claim {ClaimId}", request.ClaimId);

        // INTERVIEW: Run specialist agents — order matters here
        // Validate data first (fast, cheap) before expensive policy/fraud checks
        // If validation fails → stop immediately, no point checking policy

        // STEP 1: Validate claim data completeness
        var validationResult = await _validator.ValidateAsync(request);
        _logger.LogInformation("[SUPERVISOR] Validation: {Status}", validationResult.IsValid);

        if (!validationResult.IsValid)
        {
            // INTERVIEW: Fail fast — don't run expensive agents if basic validation fails
            return new SupervisorDecision
            {
                ClaimId   = request.ClaimId,
                Decision  = "denied",
                Reason    = $"Validation failed: {validationResult.FailureReason}",
                Escalate  = false
            };
        }

        // STEP 2: Run PolicyChecker + FraudDetector in PARALLEL
        // INTERVIEW: Independent sub-agents can run concurrently — saves latency
        // Policy check and fraud detection don't depend on each other's results
        var (policyResult, fraudResult) = await (
            _policyChecker.CheckAsync(request),
            _fraudDetector.AnalyzeAsync(request)
        );

        _logger.LogInformation(
            "[SUPERVISOR] Policy: {PolicyOk} | Fraud risk: {FraudScore}",
            policyResult.MeetsCriteria, fraudResult.RiskScore);

        // STEP 3: SYNTHESIZE — Supervisor makes the final call
        // INTERVIEW: Supervisor has the full picture — all specialist results
        // It applies business rules across all results to make the final decision

        // High fraud risk → always escalate regardless of policy
        if (fraudResult.RiskScore > 0.75)
        {
            _logger.LogWarning("[SUPERVISOR] High fraud risk {Score} — escalating", fraudResult.RiskScore);
            return new SupervisorDecision
            {
                ClaimId  = request.ClaimId,
                Decision = "escalated",
                Reason   = $"Fraud risk score {fraudResult.RiskScore:P0} exceeds threshold. Human review required.",
                Escalate = true
            };
        }

        // Policy criteria not met → deny
        if (!policyResult.MeetsCriteria)
        {
            return new SupervisorDecision
            {
                ClaimId  = request.ClaimId,
                Decision = "denied",
                Reason   = policyResult.DenialReason,
                Escalate = false
            };
        }

        // All checks passed → approve
        return new SupervisorDecision
        {
            ClaimId   = request.ClaimId,
            Decision  = "approved",
            Reason    = $"Dealer eligible, policy criteria met ({policyResult.PolicyRef}), fraud risk low ({fraudResult.RiskScore:P0})",
            PolicyRef = policyResult.PolicyRef,
            Escalate  = false
        };
    }
}

// INTERVIEW: Failure propagation — if a sub-agent fails, supervisor decides how to handle
// Options: retry, use cached result, skip (if non-critical), or escalate
// In healthcare: if ClinicalCriteriaAgent fails → always escalate, never skip
public record SupervisorDecision
{
    public string ClaimId   { get; init; } = string.Empty;
    public string Decision  { get; init; } = string.Empty;
    public string Reason    { get; init; } = string.Empty;
    public string PolicyRef { get; init; } = string.Empty;
    public bool   Escalate  { get; init; }
}
