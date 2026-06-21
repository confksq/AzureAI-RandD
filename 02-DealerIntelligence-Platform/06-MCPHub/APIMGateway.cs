// ============================================================
// MODULE 05: MCP Hub — APIM Gateway Integration
// ============================================================
// WHAT: Azure API Management (APIM) sits in front of the MCP Hub
//       Handles: auth, rate limiting, throttling, logging, versioning
// WHY:  MCP Hub handles tool routing; APIM handles enterprise concerns
//       APIM = enterprise policy layer; MCP Hub = AI-native routing
//       They complement each other — not one replacing the other
// JMA:  APIM enforces: JWT auth, 100 req/min per dealer, audit logs
// HEALTHCARE EQUIVALENT: APIM enforces HIPAA audit requirements,
//       mTLS between services, rate limits to prevent EHR overload
// ============================================================
// INTERVIEW: "When would you use APIM vs MCP Hub?"
// "Both. APIM handles enterprise concerns — auth, rate limits, versioning,
//  audit. MCP Hub handles AI-native concerns — tool discovery, agent routing,
//  semantic metadata. They layer: request hits APIM first (validate JWT,
//  throttle, log), then routes to MCP Hub (discover correct tool, route).
//  In healthcare: APIM enforces HIPAA audit on every tool call;
//  MCP Hub routes to the right clinical data connector."
// ============================================================

namespace DealerIntelligence.MCPHub;

public class APIMGateway
{
    private readonly HttpClient            _http;
    private readonly MCPToolRegistry       _registry;
    private readonly ILogger<APIMGateway>  _logger;

    public APIMGateway(HttpClient http, MCPToolRegistry registry, ILogger<APIMGateway> logger)
    {
        _http     = http;
        _registry = registry;
        _logger   = logger;
    }

    /// <summary>
    /// Validates the incoming agent request through APIM policies before
    /// routing to the MCP Hub tool registry.
    /// INTERVIEW: APIM policies run BEFORE the request reaches MCP.
    /// </summary>
    public async Task<MCPToolResult> HandleRequestAsync(MCPToolCall call, string jwtToken)
    {
        // APIM Policy 1: Validate JWT (in production: APIM validates automatically)
        if (!ValidateToken(jwtToken, out var agentId))
        {
            _logger.LogWarning("[APIM] Invalid JWT from agent {AgentId}", call.AgentId);
            return MCPToolResult.Failure("Unauthorized: invalid JWT");
        }

        // APIM Policy 2: Rate limit check (in production: APIM enforces automatically)
        // Example: 100 tool calls per minute per agent
        if (!await CheckRateLimitAsync(agentId))
        {
            _logger.LogWarning("[APIM] Rate limit exceeded for agent {AgentId}", agentId);
            return MCPToolResult.Failure("Rate limit exceeded: max 100 calls per minute");
        }

        // APIM Policy 3: Log every tool call for audit (HIPAA requirement in healthcare)
        _logger.LogInformation(
            "[APIM AUDIT] Agent: {AgentId} | Tool: {Tool} | Correlation: {Corr} | Time: {Time}",
            agentId, call.ToolName, call.CorrelationId, DateTime.UtcNow);

        // Route to MCP Hub
        return await _registry.InvokeAsync(call);
    }

    private bool ValidateToken(string jwt, out string agentId)
    {
        // Production: Microsoft.IdentityModel.Tokens JWT validation
        agentId = "agent-validated";
        return !string.IsNullOrEmpty(jwt);
    }

    private async Task<bool> CheckRateLimitAsync(string agentId)
    {
        // Production: Redis sliding window counter
        await Task.CompletedTask;
        return true;
    }
}
