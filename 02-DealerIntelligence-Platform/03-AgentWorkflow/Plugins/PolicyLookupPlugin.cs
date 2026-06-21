// ============================================================
// MODULE 06: KernelFunction Plugin — RAG Tool Call
// ============================================================
// WHAT: Retrieves relevant incentive policy chunks from Azure AI Search
//       This is the RAG retrieval step — happens INSIDE an agent tool call
// WHY:  Agent needs to know the rules for the specific incentive program
//       before it can decide to approve or deny
// JMA:  Searches JMA policy documents indexed in Azure AI Search
// HEALTHCARE EQUIVALENT: get_formulary_policy — retrieves payer policy
//       chunks (prior auth criteria, step therapy requirements)
// ============================================================

using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace DealerIntelligence.AgentWorkflow.Plugins;

public class PolicyLookupPlugin
{
    private readonly HybridRetrieval _search;
    private readonly EmbeddingService _embeddings;

    public PolicyLookupPlugin(HybridRetrieval search, EmbeddingService embeddings)
    {
        _search     = search;
        _embeddings = embeddings;
    }

    [KernelFunction("lookup_incentive_policy")]
    [Description("Retrieves the policy rules and eligibility criteria for a specific incentive program. Use this to understand what conditions must be met for a claim to be approved.")]
    public async Task<PolicyResult> LookupPolicyAsync(
        [Description("The incentive program code to look up policy for")] string programCode,
        [Description("Specific aspect of policy to find, e.g., 'eligibility criteria', 'claim deadline', 'maximum amount'")] string policyQuestion)
    {
        // INTERVIEW: Embed the question first, then hybrid search
        // The embedding converts the question into a vector for semantic search
        var queryEmbedding = await _embeddings.EmbedAsync(policyQuestion);

        // INTERVIEW: Hybrid retrieval = keyword + vector + RRF
        // Filter by programCode so we only get policy for THIS program
        var chunks = await _search.SearchAsync(
            query:             policyQuestion,
            queryEmbedding:    queryEmbedding,
            programCodeFilter: programCode,
            topK:              3);  // Top 3 chunks = enough context, controlled token cost

        if (!chunks.Any())
            return new PolicyResult
            {
                Found   = false,
                Content = $"No policy found for program {programCode}. Escalate to program administrator."
            };

        // INTERVIEW: Concatenate chunks into context for the LLM
        // The LLM reads these chunks in the Observe step and uses them to reason
        return new PolicyResult
        {
            Found      = true,
            ProgramCode= programCode,
            Content    = string.Join("\n\n---\n\n", chunks.Select(c => c.Content)),
            Sources    = chunks.Select(c => c.DocType).Distinct().ToList()
        };
        // INTERVIEW: "Sources" = citation. Agent can say "per program policy document section X"
        // This is grounding — every claim in the answer has a verifiable source
    }
}

public record PolicyResult
{
    public bool         Found       { get; init; }
    public string       ProgramCode { get; init; } = string.Empty;
    public string       Content     { get; init; } = string.Empty;
    public List<string> Sources     { get; init; } = [];
}
