// ============================================================
// MODULE 06: End-to-End Agent Workflow — CENTERPIECE
// ============================================================
// WHAT: The Incentive Claim Agent — receives a claim, reasons about it,
//       calls plugins in a ReAct loop, validates output, returns decision
// WHY:  This is the core agent pattern — SK orchestrates the ReAct loop
//       (Reason → Act → Observe → Reason again) until task is complete
// JMA:  Processes dealer incentive claims: check eligibility → lookup policy
//       → validate data → write decision to DMS
// HEALTHCARE EQUIVALENT: Prior Authorization agent — check patient eligibility
//       → lookup formulary policy → validate clinical criteria → write auth decision
// ============================================================
// INTERVIEW: "Walk me through your end-to-end agent workflow"
// Answer structure: Receive → Reason → Plan → Retrieve → Tool Call
//                  → Observe → Loop → Generate → Validate → Respond → Monitor
// ============================================================

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Azure.Identity;

namespace DealerIntelligence.AgentWorkflow;

public class IncentiveClaimAgent
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chat;
    private readonly ILogger<IncentiveClaimAgent> _logger;

    public IncentiveClaimAgent(
        string azureOpenAIEndpoint,
        string deploymentName,
        ILogger<IncentiveClaimAgent> logger)
    {
        _logger = logger;

        // INTERVIEW: Build the Kernel — the container for plugins + LLM service
        // Managed Identity = no API keys in code, works in Azure automatically
        var builder = Kernel.CreateBuilder()
            .AddAzureOpenAIChatCompletion(
                deploymentName,
                azureOpenAIEndpoint,
                new DefaultAzureCredential());  // Managed Identity

        // Register all plugins (tools the agent can call)
        builder.Plugins.AddFromType<DealerEligibilityPlugin>();
        builder.Plugins.AddFromType<PolicyLookupPlugin>();
        builder.Plugins.AddFromType<ClaimDecisionPlugin>();

        // INTERVIEW: FunctionInvocationFilter = audit every tool call
        // Logs: function name, inputs, outputs, latency, token count
        // This is your HIPAA-equivalent audit trail at JM Family
        builder.Services.AddSingleton<IFunctionInvocationFilter, AuditFilter>();

        _kernel = builder.Build();
        _chat   = _kernel.GetRequiredService<IChatCompletionService>();
    }

    /// <summary>
    /// Processes an incentive claim end-to-end using the ReAct agent loop.
    /// HEALTHCARE EQUIVALENT: ProcessPriorAuthRequest
    /// </summary>
    public async Task<ClaimDecision> ProcessClaimAsync(
        ClaimRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Processing claim for dealer: {DealerId}", request.DealerId);

        // STEP 1 — RECEIVE: Build the task payload
        // INTERVIEW: System prompt defines WHO the agent is, WHAT it can do,
        //            WHAT rules it follows, and WHAT format to output
        var history = new ChatHistory(SystemPrompts.IncentiveClaimAgent);

        // STEP 2 — REASON: Give the agent the task
        history.AddUserMessage($"""
            Process this incentive claim:
            Dealer ID: {request.DealerId}
            Program Code: {request.ProgramCode}
            Vehicle VIN: {request.VehicleVin}
            Claim Amount: ${request.ClaimAmount:F2}
            Sale Date: {request.SaleDate:yyyy-MM-dd}

            Verify dealer eligibility, check program policy, validate the claim,
            and return a structured approval or denial decision.
            """);

        // STEP 3 — PLAN + LOOP: ReAct loop — agent decides which plugins to call
        // INTERVIEW: MaxAutoInvokeAttempts = hard stop — agent cannot loop forever
        // If not resolved in 10 iterations → escalation triggered automatically
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior      = ToolCallBehavior.AutoInvokeKernelFunctions,
            MaxAutoInvokeAttempts = 10,   // WHY: Prevents infinite loops in production
            Temperature           = 0.0,  // WHY: 0 = deterministic for approval decisions
        };

        // STEP 4-7 — RETRIEVE + TOOL CALLS + OBSERVE + LOOP:
        // SK automatically manages the ReAct loop:
        // Iteration 1: agent reasons → calls DealerEligibilityPlugin → observes result
        // Iteration 2: agent reasons → calls PolicyLookupPlugin → observes result
        // Iteration 3: agent reasons → calls ClaimDecisionPlugin → observes result → DONE
        var response = await _chat.GetChatMessageContentAsync(
            history, executionSettings, _kernel, ct);

        // STEP 8-9 — GENERATE + VALIDATE: Parse and validate the structured output
        // INTERVIEW: Never trust raw LLM output in production — always validate
        var decision = ParseAndValidate(response.Content ?? string.Empty, request);

        _logger.LogInformation("Claim {ClaimId} decision: {Decision}",
            request.ClaimId, decision.Status);

        return decision;
    }

    private ClaimDecision ParseAndValidate(string llmOutput, ClaimRequest request)
    {
        // INTERVIEW: Output validator checks schema before writing to DMS
        // If validation fails → route to human reviewer, never write bad data
        try
        {
            // Parse expected JSON output (model fine-tuned to always output this schema)
            var decision = System.Text.Json.JsonSerializer.Deserialize<ClaimDecision>(llmOutput)
                ?? throw new InvalidOperationException("Empty decision output");

            // Validate required fields
            if (string.IsNullOrEmpty(decision.Status))
                throw new InvalidOperationException("Missing decision status");

            if (decision.Status == "approved" && string.IsNullOrEmpty(decision.AuthNumber))
                throw new InvalidOperationException("Approved decision missing auth number");

            return decision;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Output validation failed: {Error} — routing to RSM review", ex.Message);
            // INTERVIEW: Failed validation = escalate, never guess
            return new ClaimDecision
            {
                ClaimId    = request.ClaimId,
                Status     = "escalated",
                Rationale  = $"Auto-validation failed: {ex.Message}. Routed to Regional Sales Manager.",
                AuthNumber = string.Empty
            };
        }
    }
}

public record ClaimRequest
{
    public string   ClaimId      { get; init; } = Guid.NewGuid().ToString();
    public string   DealerId     { get; init; } = string.Empty;
    public string   ProgramCode  { get; init; } = string.Empty;
    public string   VehicleVin   { get; init; } = string.Empty;
    public double   ClaimAmount  { get; init; }
    public DateTime SaleDate     { get; init; }
}

public record ClaimDecision
{
    public string ClaimId    { get; init; } = string.Empty;
    public string Status     { get; init; } = string.Empty;  // approved | denied | escalated
    public string Rationale  { get; init; } = string.Empty;
    public string AuthNumber { get; init; } = string.Empty;
    public string PolicyRef  { get; init; } = string.Empty;
}
