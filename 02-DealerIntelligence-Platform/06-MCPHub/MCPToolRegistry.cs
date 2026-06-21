// ============================================================
// MODULE 05: MCP Hub — Tool Discovery and Registration
// ============================================================
// WHAT: MCP Hub is the central registry where tools advertise
//       their capabilities. Agents query the hub to discover
//       what tools are available without knowing backend details.
// WHY:  Without MCP: each agent hardcodes connections to each tool
//       With MCP Hub: agents ask "what can you do?" and hub routes
//       N agents × M tools = N×M connections → reduced to N+M
// JMA:  Hub registers: DMS tool, Policy Search tool, DI tool
// HEALTHCARE EQUIVALENT: Hub registers: FHIR tool, Payer API tool,
//       Formulary tool, Lab system tool, EHR write tool
// ============================================================
// INTERVIEW: "What is MCP Hub?"
// "MCP is an open standard for AI agent-to-tool communication.
//  The Hub is the central gateway — agents connect to one place
//  and discover all available tools. When Hospital X upgrades
//  their EHR API, I update one connector in the MCP Hub —
//  not 12 agents. It's the difference between N×M and N+M."
// ============================================================

namespace DealerIntelligence.MCPHub;

/// <summary>
/// MCP Tool Registry — central catalogue of all tools available to agents.
/// Agents call DiscoverToolsAsync() to find what they can use.
/// </summary>
public class MCPToolRegistry
{
    private readonly Dictionary<string, MCPToolDefinition> _tools = new();
    private readonly ILogger<MCPToolRegistry> _logger;

    public MCPToolRegistry(ILogger<MCPToolRegistry> logger) => _logger = logger;

    /// <summary>
    /// Registers a tool in the MCP Hub.
    /// Called at startup for each backend capability.
    /// INTERVIEW: This is how tools "advertise" — name, description, input/output schema
    /// The LLM reads these definitions to decide which tool to call
    /// </summary>
    public void RegisterTool(MCPToolDefinition tool)
    {
        _tools[tool.Name] = tool;
        _logger.LogInformation("[MCP] Registered tool: {ToolName} v{Version}", tool.Name, tool.Version);
    }

    /// <summary>
    /// Agent calls this to discover available tools.
    /// INTERVIEW: Discovery is what makes MCP different from hardcoded tool lists.
    /// Agent doesn't need to know tools at compile time — it discovers at runtime.
    /// </summary>
    public IReadOnlyList<MCPToolDefinition> DiscoverTools(string? category = null)
    {
        return _tools.Values
            .Where(t => category == null || t.Category == category)
            .ToList();
    }

    /// <summary>
    /// Routes a tool call from an agent to the correct backend handler.
    /// INTERVIEW: Hub handles routing — agent just calls the tool name, hub knows where to send it
    /// </summary>
    public async Task<MCPToolResult> InvokeAsync(MCPToolCall call)
    {
        if (!_tools.TryGetValue(call.ToolName, out var tool))
        {
            _logger.LogError("[MCP] Unknown tool: {ToolName}", call.ToolName);
            return MCPToolResult.Failure($"Tool '{call.ToolName}' not registered in MCP Hub");
        }

        _logger.LogInformation("[MCP] Routing call → {ToolName} | Agent: {AgentId}",
            call.ToolName, call.AgentId);

        try
        {
            var result = await tool.Handler(call.Parameters);
            return MCPToolResult.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MCP] Tool call failed: {ToolName}", call.ToolName);
            return MCPToolResult.Failure(ex.Message);
        }
    }
}

public record MCPToolDefinition
{
    public string   Name        { get; init; } = string.Empty;
    public string   Description { get; init; } = string.Empty;  // LLM reads this for tool selection
    public string   Category    { get; init; } = string.Empty;  // e.g., "dealer", "policy", "document"
    public string   Version     { get; init; } = "1.0";
    public object   InputSchema { get; init; } = new { };       // JSON Schema for parameters
    public object   OutputSchema{ get; init; } = new { };       // JSON Schema for return value
    public Func<Dictionary<string, object>, Task<object>> Handler { get; init; } = _ => Task.FromResult<object>(new { });
}

public record MCPToolCall
{
    public string AgentId    { get; init; } = string.Empty;
    public string ToolName   { get; init; } = string.Empty;
    public Dictionary<string, object> Parameters { get; init; } = new();
    public string CorrelationId { get; init; } = string.Empty;
}

public record MCPToolResult
{
    public bool   Success { get; init; }
    public object Data    { get; init; } = new { };
    public string Error   { get; init; } = string.Empty;

    public static MCPToolResult Success(object data)  => new() { Success = true,  Data = data };
    public static MCPToolResult Failure(string error) => new() { Success = false, Error = error };
}
