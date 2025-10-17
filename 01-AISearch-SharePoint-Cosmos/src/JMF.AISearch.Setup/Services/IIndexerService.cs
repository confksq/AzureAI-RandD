namespace JMF.AISearch.Setup.Services;

public interface IIndexerService
{
    Task RunIndexerAsync(string indexerName, CancellationToken ct = default);
    Task<string> GetIndexerStatusAsync(string indexerName, CancellationToken ct = default);
    Task ResetIndexerAsync(string indexerName, CancellationToken ct = default);
}
