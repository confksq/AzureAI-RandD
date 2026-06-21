// ============================================================
// MODULE 06: FunctionInvocationFilter — Audit Every Tool Call
// ============================================================
// WHAT: Intercepts every plugin/tool call the agent makes
//       Logs: function name, inputs, outputs, latency, tokens
// WHY:  In production you need to know exactly what the agent did
//       at every step — for debugging, compliance, and cost control
// JMA:  Every dealer claim tool call logged to App Insights
// HEALTHCARE EQUIVALENT: HIPAA audit trail — log every PHI access
// ============================================================
// INTERVIEW: "How do you audit your agent's tool calls?"
// "We use FunctionInvocationFilter in Semantic Kernel — it intercepts
//  every tool call before and after execution. We log the function name,
//  input parameters, output, latency, and token count to App Insights.
//  In a healthcare context this is your HIPAA audit trail — you know
//  exactly which PHI was accessed, when, and by which agent."
// ============================================================

using Microsoft.SemanticKernel;
using System.Diagnostics;

namespace DealerIntelligence.AgentWorkflow;

public class AuditFilter : IFunctionInvocationFilter
{
    private readonly ILogger<AuditFilter> _logger;
    private readonly TelemetryClient _telemetry;

    public AuditFilter(ILogger<AuditFilter> logger, TelemetryClient telemetry)
    {
        _logger    = logger;
        _telemetry = telemetry;
    }

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        var sw = Stopwatch.StartNew();

        // BEFORE tool call — log what agent is about to do
        _logger.LogInformation(
            "[AGENT TOOL CALL] Function: {Plugin}.{Function} | Args: {Args}",
            context.Function.PluginName,
            context.Function.Name,
            System.Text.Json.JsonSerializer.Serialize(context.Arguments));

        try
        {
            // Execute the tool call
            await next(context);
            sw.Stop();

            // AFTER tool call — log what the tool returned
            _logger.LogInformation(
                "[AGENT TOOL RESULT] Function: {Plugin}.{Function} | Result: {Result} | Latency: {Ms}ms",
                context.Function.PluginName,
                context.Function.Name,
                context.Result?.ToString()?[..Math.Min(200, context.Result.ToString()?.Length ?? 0)],
                sw.ElapsedMilliseconds);

            // INTERVIEW: Track to App Insights for dashboards and alerting
            _telemetry.TrackEvent("AgentToolCall", new Dictionary<string, string>
            {
                ["plugin"]    = context.Function.PluginName ?? "unknown",
                ["function"]  = context.Function.Name,
                ["latency_ms"]= sw.ElapsedMilliseconds.ToString(),
                ["success"]   = "true"
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "[AGENT TOOL FAILED] Function: {Plugin}.{Function} | Error: {Error}",
                context.Function.PluginName,
                context.Function.Name,
                ex.Message);

            _telemetry.TrackEvent("AgentToolCallFailed", new Dictionary<string, string>
            {
                ["plugin"]   = context.Function.PluginName ?? "unknown",
                ["function"] = context.Function.Name,
                ["error"]    = ex.Message
            });

            throw; // re-throw so fault tolerance layer can handle retry/escalation
        }
    }
}
