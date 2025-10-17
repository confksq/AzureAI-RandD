using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Logging;

namespace JMF.AISearch.Setup.Services;

public sealed class IndexerService(
    SearchIndexerClient indexerClient,
    ILogger<IndexerService> logger) : IIndexerService
{
    public async Task RunIndexerAsync(string indexerName, CancellationToken ct = default)
    {
        await indexerClient.RunIndexerAsync(indexerName, ct);
        logger.LogInformation("Indexer '{Name}' triggered.", indexerName);
    }

    public async Task<string> GetIndexerStatusAsync(string indexerName, CancellationToken ct = default)
    {
        var response = await indexerClient.GetIndexerStatusAsync(indexerName, ct);
        var status = response.Value;

        var lastRun = status.LastResult;
        var summary = lastRun is null
            ? "No runs recorded"
            : $"Status={lastRun.Status} | ItemsProcessed={lastRun.ItemCount} | Failed={lastRun.FailedItemCount} | End={lastRun.EndTime:u}";

        logger.LogInformation("Indexer '{Name}': {Summary}", indexerName, summary);
        return summary;
    }

    public async Task ResetIndexerAsync(string indexerName, CancellationToken ct = default)
    {
        await indexerClient.ResetIndexerAsync(indexerName, ct);
        logger.LogInformation("Indexer '{Name}' reset. Next run will reindex all documents.", indexerName);
    }
}
