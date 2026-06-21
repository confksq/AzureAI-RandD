// ============================================================
// RAG PIPELINE: Hybrid Retrieval = Vector + Keyword + Rerank
// ============================================================
// WHAT: Combines two search types + semantic re-ranking:
//       1. Vector search (HNSW cosine similarity) — finds semantically similar
//       2. BM25 keyword search — finds exact term matches
//       3. RRF fusion — merges both result sets
//       4. Semantic re-ranking — final quality sort using language model
// WHY:  Vector search misses exact terms: "Program Code GC-2026-Q1"
//       Keyword search misses meaning: "conquest program" ≠ "conquest incentive"
//       Hybrid + rerank = best of both worlds
// JMA:  Query "Gulf Conquest eligibility max" → vector finds similar text,
//       keyword finds exact program code → RRF merges → reranker picks best
// HEALTHCARE EQUIVALENT: "formulary tier for lisinopril 10mg" → vector finds
//       semantically similar formulary entries, keyword finds exact drug name
// ============================================================
// INTERVIEW: "Why hybrid retrieval instead of just vector search?"
// "Vector search alone misses exact terms like drug codes, program codes,
//  procedure codes. Keyword search misses semantic equivalents — 'conquest'
//  and 'conquest incentive program' have different BM25 scores but mean
//  the same thing. Hybrid gives you both. The RRF (Reciprocal Rank Fusion)
//  algorithm merges the two result lists — each result gets score 1/(k+rank)
//  from each list, then scores are summed. Simple but very effective.
//  Semantic re-ranking is the final quality filter — the LLM reads each
//  retrieved chunk and decides if it's actually relevant to the question."
// ============================================================

using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Azure.Identity;

namespace DealerIntelligence.RAGSearch;

public class HybridRetrieval
{
    private readonly SearchClient _searchClient;
    private readonly IEmbeddingService _embedder;
    private readonly ILogger<HybridRetrieval> _logger;

    public HybridRetrieval(
        string searchEndpoint,
        IEmbeddingService embedder,
        ILogger<HybridRetrieval> logger)
    {
        _searchClient = new SearchClient(
            new Uri(searchEndpoint),
            "dealer-policy-index",
            new Azure.Identity.DefaultAzureCredential());
        _embedder = embedder;
        _logger   = logger;
    }

    /// <summary>
    /// Full hybrid retrieval pipeline: embed query → hybrid search → semantic rerank.
    /// INTERVIEW: This is the "R" in RAG — Retrieve.
    /// The quality of your retrieved chunks determines the quality of your generation.
    /// </summary>
    public async Task<List<RetrievedChunk>> RetrieveAsync(string query, string? programCodeFilter = null, int topK = 5)
    {
        _logger.LogInformation("[HYBRID] Query: '{Query}' | Program filter: {Program}", query, programCodeFilter);

        // STEP 1: Embed the query — same model that embedded the policy chunks
        // INTERVIEW: CRITICAL — must use the SAME embedding model for query and chunks
        // Different models = different vector spaces = no meaningful cosine similarity
        var queryEmbedding = await _embedder.EmbedAsync(query);

        // STEP 2: Hybrid search — vector + keyword + RRF fusion + semantic rerank
        var options = new SearchOptions
        {
            // INTERVIEW: VectorSearch = HNSW cosine similarity search
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryEmbedding)
                    {
                        KNearestNeighborsCount = topK * 2,  // retrieve 2x, reranker will filter
                        Fields = { "embedding" }
                    }
                }
            },

            // INTERVIEW: QueryType.Semantic activates semantic re-ranking
            // This runs AFTER vector+keyword retrieval — it re-reads the text and re-scores
            QueryType = SearchQueryType.Semantic,
            SemanticSearch = new SemanticSearchOptions
            {
                ConfigurationName = "dealer-semantic",
                // INTERVIEW: SemanticRanker reads the top results and re-scores by meaning
                // It uses a cross-encoder model — compares query against each chunk directly
                // More expensive than HNSW but higher quality for final ranking
            },

            // INTERVIEW: Filter = pre-filter before search, reduces search space
            // Filtering on programCode means HNSW only searches within that program's chunks
            Filter = programCodeFilter != null ? $"programCode eq '{programCodeFilter}'" : null,

            // INTERVIEW: Select only the fields you need — reduces response size
            Select = { "id", "text", "documentId", "parentId", "programCode" },
            Size   = topK
        };

        // INTERVIEW: The search text here drives BM25 keyword search
        // BM25 = term frequency + inverse document frequency (same as Elasticsearch)
        // Azure AI Search runs BM25 + HNSW in parallel, then RRF merges
        var results = await _searchClient.SearchAsync<PolicyChunk>(query, options);

        var chunks = new List<RetrievedChunk>();
        await foreach (var result in results.Value.GetResultsAsync())
        {
            var semanticScore = result.SemanticSearch?.RerankerScore;
            chunks.Add(new RetrievedChunk
            {
                Id           = result.Document.Id,
                Text         = result.Document.Text,
                DocumentId   = result.Document.DocumentId,
                ProgramCode  = result.Document.ProgramCode,
                HybridScore  = result.Score ?? 0.0,
                SemanticScore = semanticScore ?? 0.0,
                // INTERVIEW: SemanticCaptions = highlighted key phrases from the chunk
                //            Useful for showing the user WHICH part of the chunk matched
                KeyPhrases   = result.SemanticSearch?.Captions.Select(c => c.Text).ToArray() ?? []
            });
        }

        _logger.LogInformation("[HYBRID] Retrieved {Count} chunks for query '{Query}'", chunks.Count, query);
        return chunks;
    }
}

public record RetrievedChunk
{
    public string   Id            { get; init; } = string.Empty;
    public string   Text          { get; init; } = string.Empty;
    public string   DocumentId    { get; init; } = string.Empty;
    public string   ProgramCode   { get; init; } = string.Empty;
    public double   HybridScore   { get; init; }   // INTERVIEW: RRF-fused score (vector + BM25)
    public double   SemanticScore { get; init; }   // INTERVIEW: Semantic re-ranker score (higher = more relevant)
    public string[] KeyPhrases    { get; init; } = [];  // Semantic captions
}

// Stub interface — implemented by Azure OpenAI embedding service
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text);
}
