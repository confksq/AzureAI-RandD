// ============================================================
// MODULE 06: KernelFunction Plugin — Write Decision to DMS
// ============================================================
// WHAT: Final plugin in the ReAct loop — writes the approved/denied
//       decision back to the Dealer Management System
// WHY:  The agent's job isn't done until the decision is persisted
//       This is the "action" step that changes real system state
// JMA:  Writes claim decision to JMA DMS with auth number + rationale
// HEALTHCARE EQUIVALENT: submit_auth_decision — writes prior auth
//       approval/denial to EHR with auth number and clinical rationale
// ============================================================
// INTERVIEW: "What happens at the end of the agent loop?"
// "The final plugin writes the structured decision to the backend system.
//  In our prior auth equivalent, that's writing the approval decision
//  back to the EHR with a generated auth number, rationale citing the
//  policy source, and expiry date. The agent never considers itself done
//  until the decision is persisted and confirmed by the DMS."
// ============================================================

using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace DealerIntelligence.AgentWorkflow.Plugins;

public class ClaimDecisionPlugin
{
    private readonly HttpClient _dmsClient;
    private readonly ILogger<ClaimDecisionPlugin> _logger;

    public ClaimDecisionPlugin(IHttpClientFactory factory, ILogger<ClaimDecisionPlugin> logger)
    {
        _dmsClient = factory.CreateClient("DMS");
        _logger    = logger;
    }

    [KernelFunction("submit_claim_decision")]
    [Description("Submits the final approval or denial decision for an incentive claim to the DMS. Only call this after verifying dealer eligibility AND confirming policy criteria are met or not met.")]
    public async Task<SubmissionResult> SubmitDecisionAsync(
        [Description("The claim ID to submit decision for")]           string claimId,
        [Description("Decision: 'approved' or 'denied'")]              string decision,
        [Description("Clear rationale citing specific policy criteria")] string rationale,
        [Description("The policy document and section that supports this decision")] string policyReference)
    {
        // INTERVIEW: Validate decision before writing to production system
        if (decision != "approved" && decision != "denied")
            throw new ArgumentException($"Invalid decision value: {decision}. Must be 'approved' or 'denied'");

        var payload = new
        {
            ClaimId        = claimId,
            Decision       = decision,
            Rationale      = rationale,
            PolicyRef      = policyReference,
            ProcessedAt    = DateTime.UtcNow,
            ProcessedBy    = "IncentiveClaimAgent-v2",
            // Generate auth number for approved claims
            AuthNumber     = decision == "approved"
                ? $"JMA-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString()[..8].ToUpper()}"
                : string.Empty
        };

        var response = await _dmsClient.PostAsJsonAsync("/api/claims/decisions", payload);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation(
            "Decision submitted: Claim {ClaimId} → {Decision} | Auth: {AuthNumber}",
            claimId, decision, payload.AuthNumber);

        return new SubmissionResult
        {
            Success    = true,
            AuthNumber = payload.AuthNumber,
            Message    = $"Claim {claimId} {decision}. {(payload.AuthNumber != string.Empty ? $"Auth number: {payload.AuthNumber}" : string.Empty)}"
        };
    }

    [KernelFunction("escalate_claim")]
    [Description("Escalates a claim to a Regional Sales Manager for human review. Use this when eligibility is unclear, policy is ambiguous, or claim amount exceeds auto-approval threshold.")]
    public async Task<SubmissionResult> EscalateAsync(
        [Description("The claim ID to escalate")]           string claimId,
        [Description("Reason for escalation")]              string reason,
        [Description("Priority level: 'standard' or 'urgent'")] string priority = "standard")
    {
        // INTERVIEW: Escalation = agent acknowledging it cannot complete the task
        // Better to escalate than to guess — especially for high-value claims
        var ticketId = $"ESC-{DateTime.UtcNow:yyyyMMdd}-{claimId[..8]}";

        _logger.LogWarning(
            "Claim {ClaimId} escalated to RSM review. Reason: {Reason} | Ticket: {Ticket}",
            claimId, reason, ticketId);

        // In production: send to Service Bus queue → RSM notification system
        await _dmsClient.PostAsJsonAsync("/api/claims/escalations", new
        {
            ClaimId   = claimId,
            Reason    = reason,
            Priority  = priority,
            TicketId  = ticketId,
            CreatedAt = DateTime.UtcNow
        });

        return new SubmissionResult
        {
            Success    = true,
            AuthNumber = string.Empty,
            Message    = $"Claim escalated to RSM review. Ticket ID: {ticketId}. Expected response: 2 business days."
        };
    }
}

public record SubmissionResult
{
    public bool   Success    { get; init; }
    public string AuthNumber { get; init; } = string.Empty;
    public string Message    { get; init; } = string.Empty;
}
