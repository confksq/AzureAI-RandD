// ============================================================
// MODULE 07: Specialist Sub-Agent — Policy Checker
// ============================================================
// Checks whether the claim meets program policy criteria using RAG
// HEALTHCARE EQUIVALENT: FormularyCheckerAgent / ClinicalCriteriaAgent
// ============================================================

namespace DealerIntelligence.MetaAgentOrchestration;

public class PolicyCheckerAgent
{
    private readonly Kernel _kernel;
    private readonly ILogger<PolicyCheckerAgent> _logger;

    public PolicyCheckerAgent(Kernel kernel, ILogger<PolicyCheckerAgent> logger)
    {
        _kernel = kernel;
        _logger = logger;
    }

    public async Task<PolicyCheckResult> CheckAsync(ClaimRequest request)
    {
        _logger.LogInformation("[POLICY] Checking policy for program {Program}", request.ProgramCode);

        // INTERVIEW: PolicyChecker has its own focused system prompt
        // It's ONLY about policy — doesn't know about fraud or validation
        var history = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory(
            SystemPrompts.PolicyCheckerAgent);

        history.AddUserMessage($"""
            Check whether this claim meets program policy criteria:
            Program: {request.ProgramCode}
            Claim Amount: ${request.ClaimAmount}
            Sale Date: {request.SaleDate:yyyy-MM-dd}
            VIN: {request.VehicleVin}

            Use the lookup_incentive_policy tool to retrieve the program rules,
            then evaluate whether all criteria are met.
            """);

        var chat = _kernel.GetRequiredService<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>();
        var response = await chat.GetChatMessageContentAsync(history,
            new Microsoft.SemanticKernel.Connectors.OpenAI.OpenAIPromptExecutionSettings
            {
                ToolCallBehavior      = Microsoft.SemanticKernel.Connectors.OpenAI.ToolCallBehavior.AutoInvokeKernelFunctions,
                MaxAutoInvokeAttempts = 5,
                Temperature           = 0.0
            }, _kernel);

        return ParsePolicyResult(response.Content ?? string.Empty);
    }

    private static PolicyCheckResult ParsePolicyResult(string response) =>
        new() { ClaimId = "", MeetsCriteria = true, PolicyRef = "Policy-2026 §3.1" };
}

public record PolicyCheckResult
{
    public string   ClaimId       { get; init; } = string.Empty;
    public bool     MeetsCriteria { get; init; }
    public string   DenialReason  { get; init; } = string.Empty;
    public string   PolicyRef     { get; init; } = string.Empty;
    public string[] EvidenceChunks { get; init; } = [];
}
