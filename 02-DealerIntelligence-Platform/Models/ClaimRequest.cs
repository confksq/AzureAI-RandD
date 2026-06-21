// Shared model types used across all modules

namespace DealerIntelligence;

public record ClaimRequest
{
    public string  ClaimId     { get; init; } = string.Empty;
    public string  DealerId    { get; init; } = string.Empty;
    public string  VehicleVin  { get; init; } = string.Empty;
    public string  ProgramCode { get; init; } = string.Empty;
    public decimal ClaimAmount { get; init; }
    public DateTime SaleDate   { get; init; }
}

public record ClaimDecisionResponse
{
    public string Status    { get; init; } = string.Empty;  // approved | denied | escalated
    public string Rationale { get; init; } = string.Empty;
    public string PolicyRef { get; init; } = string.Empty;
    public string AuthNumber { get; init; } = string.Empty;
}

// Shared namespace reference for SystemPrompts used by sub-agents
namespace DealerIntelligence.PromptEngineering
{
    // PolicyCheckerAgent system prompt (used in PolicyCheckerAgent.cs)
    public static partial class SystemPrompts
    {
        public const string PolicyCheckerAgent = """
            You are the JMA Policy Checker Agent, a specialist that evaluates whether
            an incentive claim meets the program's policy criteria.

            SCOPE: You evaluate policy criteria ONLY. You do not check fraud or validate data.
            You use the lookup_incentive_policy tool to retrieve the applicable rules.

            RULES:
            - Always call lookup_incentive_policy before evaluating — never use general knowledge
            - Cite the specific policy section number in your decision
            - If policy is ambiguous, report MeetsCriteria=false with reason "Policy ambiguous — requires RSM review"

            OUTPUT FORMAT: JSON with fields: MeetsCriteria (bool), DenialReason (string), PolicyRef (string)
            """;
    }
}

// Shared reference for IncentiveClaimAgent to use in EvaluationPipeline
namespace DealerIntelligence.AgentWorkflow
{
    // Used as a type reference in EvaluationPipeline.cs
    // Real IncentiveClaimAgent is in 03-AgentWorkflow/IncentiveClaimAgent.cs
    public class IncentiveClaimAgent
    {
        public Task<ClaimDecisionResponse> ProcessClaimAsync(ClaimRequest request)
            => Task.FromResult(new ClaimDecisionResponse { Status = "approved" });
    }
}
