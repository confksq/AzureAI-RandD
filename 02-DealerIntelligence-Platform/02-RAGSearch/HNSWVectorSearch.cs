// ============================================================
// GAP TOPIC: HNSW Index — How vectors are stored and searched
// ============================================================
// WHAT: HNSW = Hierarchical Navigable Small World graph
//       The index algorithm used by Azure AI Search for vector search
//       It's an approximate nearest neighbor (ANN) algorithm
// WHY:  Brute force: compare query embedding to EVERY stored vector → slow
//       HNSW: builds a graph where similar vectors are connected
//             search traverses the graph → finds nearest neighbors fast
//             O(log n) instead of O(n) — scales to millions of chunks
// JMA:  Policy document chunks → embedded → stored in HNSW index
//       Query → embedded → HNSW returns top-K similar chunks
// HEALTHCARE EQUIVALENT: Clinical guideline chunks, formulary entries
//       stored in HNSW — retrieve relevant clinical criteria fast
// ============================================================
// INTERVIEW: "What is HNSW and why do you use it?"
// "HNSW is the vector index algorithm in Azure AI Search.
//  It builds a hierarchical graph where similar vectors are connected.
//  When a query comes in, we embed it and traverse the graph to find
//  the nearest neighbors — the most semantically similar chunks.
//  It's approximate, not exact, but the approximation is very good
//  and it scales to millions of vectors in milliseconds.
//  The key parameters are m (connections per node) and efConstruction
//  (build accuracy vs. speed trade-off during indexing)."
// ============================================================

using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Azure.Identity;

namespace DealerIntelligence.RAGSearch;

public class HNSWVectorSearch
{
    private readonly SearchIndexClient  _indexClient;
    private readonly SearchClient       _searchClient;
    private readonly ILogger<HNSWVectorSearch> _logger;

    private const string IndexName = "dealer-policy-index";

    public HNSWVectorSearch(string searchEndpoint, ILogger<HNSWVectorSearch> logger)
    {
        var credential  = new DefaultAzureCredential();
        _indexClient    = new SearchIndexClient(new Uri(searchEndpoint), credential);
        _searchClient   = new SearchClient(new Uri(searchEndpoint), IndexName, credential);
        _logger         = logger;
    }

    /// <summary>
    /// Creates the Azure AI Search index with HNSW vector configuration.
    /// Called once at setup time — not on every search.
    /// INTERVIEW: This is where you configure HNSW parameters.
    /// </summary>
    public async Task CreateIndexAsync()
    {
        _logger.LogInformation("[HNSW] Creating vector search index: {IndexName}", IndexName);

        // INTERVIEW: HNSW Algorithm Configuration
        // m = number of bi-directional links per node in the graph
        //   Higher m = better recall, larger index, slower build
        //   4 = very fast, lower recall; 16 = balanced; 64+ = high recall, slow
        // efConstruction = size of candidate list during index build
        //   Higher = better recall, slower indexing, NOT affecting search speed
        var vectorConfig = new VectorSearch
        {
            Algorithms =
            {
                new HnswAlgorithmConfiguration("hnsw-config")
                {
                    Parameters = new HnswParameters
                    {
                        M               = 4,    // INTERVIEW: connections per node (4-16 common)
                        EfConstruction  = 400,  // INTERVIEW: build accuracy (400 = good balance)
                        EfSearch        = 500,  // INTERVIEW: search accuracy (higher = better recall, slower)
                        Metric          = VectorSearchAlgorithmMetric.Cosine  // INTERVIEW: cosine = semantic similarity
                    }
                }
            },
            Profiles =
            {
                new VectorSearchProfile("vector-profile", "hnsw-config")
            }
        };

        // INTERVIEW: Index schema — notice the vector field
        // dimensions: 1536 = text-embedding-3-small dimensions
        //             3072 = text-embedding-3-large (higher quality, costs more)
        var index = new SearchIndex(IndexName)
        {
            Fields =
            {
                new SimpleField("id",         SearchFieldDataType.String)  { IsKey = true },
                new SearchableField("text")   { IsFilterable = false },     // full text of chunk
                new SimpleField("documentId", SearchFieldDataType.String)  { IsFilterable = true },
                new SimpleField("parentId",   SearchFieldDataType.String)  { IsFilterable = true },
                new SimpleField("programCode",SearchFieldDataType.String)  { IsFilterable = true },
                new SimpleField("chunkDate",  SearchFieldDataType.DateTimeOffset) { IsSortable = true },

                // INTERVIEW: This is the vector field — HNSW indexes this
                new VectorSearchField("embedding", dimensions: 1536, vectorSearchProfileName: "vector-profile")
            },
            VectorSearch = vectorConfig,

            // INTERVIEW: Semantic configuration — for semantic re-ranking after vector retrieval
            // Semantic re-ranker reads the actual text and re-scores by meaning, not just cosine similarity
            SemanticSearch = new SemanticSearch
            {
                Configurations =
                {
                    new SemanticConfiguration("dealer-semantic",
                        new SemanticPrioritizedFields
                        {
                            ContentFields = { new SemanticField("text") }
                        })
                }
            }
        };

        await _indexClient.CreateOrUpdateIndexAsync(index);
        _logger.LogInformation("[HNSW] Index created: {IndexName}", IndexName);
    }

    /// <summary>
    /// Performs pure vector search using HNSW.
    /// Returns top-K chunks by cosine similarity to the query embedding.
    /// INTERVIEW: Vector search = semantic similarity, not keyword matching
    /// "Find me text that MEANS the same thing as the query"
    /// </summary>
    public async Task<List<PolicyChunk>> SearchAsync(float[] queryEmbedding, int topK = 5)
    {
        _logger.LogDebug("[HNSW] Vector search: topK={TopK}", topK);

        var options = new SearchOptions
        {
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryEmbedding)
                    {
                        KNearestNeighborsCount = topK,  // INTERVIEW: K = number of neighbors to retrieve
                        Fields                 = { "embedding" }
                    }
                }
            },
            Size = topK
        };

        var results = await _searchClient.SearchAsync<PolicyChunk>("*", options);
        var chunks  = new List<PolicyChunk>();

        await foreach (var result in results.Value.GetResultsAsync())
        {
            var chunk = result.Document;
            chunk = chunk with { Score = result.Score ?? 0.0 };
            chunks.Add(chunk);

            _logger.LogDebug("[HNSW] Hit: {Id} | Score: {Score:F4}", chunk.Id, chunk.Score);
        }

        return chunks;
    }
}

public record PolicyChunk
{
    public string Id         { get; init; } = string.Empty;
    public string Text       { get; init; } = string.Empty;
    public string DocumentId { get; init; } = string.Empty;
    public string ParentId   { get; init; } = string.Empty;
    public string ProgramCode { get; init; } = string.Empty;
    public double Score      { get; init; }
}
