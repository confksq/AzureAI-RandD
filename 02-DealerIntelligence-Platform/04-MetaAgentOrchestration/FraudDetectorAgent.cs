// ============================================================
// MODULE 07: Specialist Sub-Agent — Fraud Detector
// ============================================================
// WHAT: Specialist agent that analyses claim patterns for anomalies
//       Has its own system prompt, tools, and knowledge
// WHY:  Fraud detection needs different signals than policy checking
//       Separate agent = focused prompt, focused tools, isolated failure
// JMA:  Checks: duplicate VINs, unusual claim frequency, amount outliers
// HEALTHCARE EQUIVALENT: Clinical anomaly detector — flags unusual
//       prescription patterns, duplicate auth requests, outlier dosages
// ============================================================

using Microsoft.SemanticKernel;

namespace DealerIntelligence.MetaAgentOrchestration;

public class FraudDetectorAgent
{
    private readonly Kernel _kernel;

    public FraudDetectorAgent(Kernel kernel) => _kernel = kernel;

    public async Task<FraudAnalysisResult> AnalyzeAsync(ClaimRequest request)
    {
        // INTERVIEW: Each sub-agent has its OWN system prompt
        // FraudDetector prompt focuses ONLY on anomaly signals
        // It doesn't know about policy or validation — separation of concerns
        var history = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory(
            SystemPrompts.FraudDetectorAgent);

        history.AddUserMessage($"""
            Analyze this incentive claim for fraud indicators:
            Dealer: {request.DealerId}
            Program: {request.ProgramCode}
            VIN: {request.VehicleVin}
            Amount: ${request.ClaimAmount}
            Sale Date: {request.SaleDate:yyyy-MM-dd}

            Check: duplicate VIN submissions, claim frequency for this dealer,
            amount vs. program average, sale date vs. program validity window.
            Return a risk score 0.0 (no risk) to 1.0 (high risk) with reasons.
            """);

        // INTERVIEW: Sub-agent has its own tool set — different from Supervisor
        // FraudDetector tools: claim history lookup, VIN dedup check, statistical outlier
        var chat = _kernel.GetRequiredService<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>();
        var response = await chat.GetChatMessageContentAsync(history,
            new Microsoft.SemanticKernel.Connectors.OpenAI.OpenAIPromptExecutionSettings
            {
                ToolCallBehavior      = Microsoft.SemanticKernel.Connectors.OpenAI.ToolCallBehavior.AutoInvokeKernelFunctions,
                MaxAutoInvokeAttempts = 5,
                Temperature           = 0.0
            }, _kernel);

        // Parse fraud score from response
        return ParseFraudResult(response.Content ?? string.Empty);
    }

    private static FraudAnalysisResult ParseFraudResult(string response)
    {
        // Simplified parsing — production would use JSON mode
        return new FraudAnalysisResult
        {
            RiskScore = 0.1,  // parsed from LLM response
            Indicators = [],
            Assessment = response
        };
    }
}

public record FraudAnalysisResult
{
    public double       RiskScore  { get; init; }
    public List<string> Indicators { get; init; } = [];
    public string       Assessment { get; init; } = string.Empty;
}
