using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using JMF.AISearch.Setup.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace JMF.AISearch.Setup.Services;

public sealed class SearchSetupService(
    SearchIndexClient indexClient,
    SearchIndexerClient indexerClient,
    IOptions<AzureSearchSettings> settings,
    ILogger<SearchSetupService> logger) : ISearchSetupService
{
    private readonly AzureSearchSettings _settings = settings.Value;

    public async Task ProvisionAllAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Starting full provisioning of Azure AI Search resources...");

        await CreateOrUpdateIndexAsync(ct);
        await CreateOrUpdateDataSourceAsync("jmf-sharepoint-datasource", ct);
        await CreateOrUpdateDataSourceAsync("jmf-cosmos-datasource", ct);
        await CreateOrUpdateSkillsetAsync(ct);
        await CreateOrUpdateIndexerAsync("jmf-sharepoint-indexer", ct);
        await CreateOrUpdateIndexerAsync("jmf-cosmos-indexer", ct);

        logger.LogInformation("Provisioning complete.");
    }

    public async Task CreateOrUpdateIndexAsync(CancellationToken ct = default)
    {
        var path = Path.Combine(_settings.InfraPath, "index", "jmf-documents-index.json");
        var json = await File.ReadAllTextAsync(path, ct);
        var definition = JsonSerializer.Deserialize<SearchIndex>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize index from {path}");

        await indexClient.CreateOrUpdateIndexAsync(definition, cancellationToken: ct);
        logger.LogInformation("Index '{IndexName}' created/updated.", definition.Name);
    }

    public async Task CreateOrUpdateDataSourceAsync(string name, CancellationToken ct = default)
    {
        var fileName = $"{name}.json";
        var path = Path.Combine(_settings.InfraPath, "datasources", fileName);
        var json = await File.ReadAllTextAsync(path, ct);
        var definition = JsonSerializer.Deserialize<SearchIndexerDataSourceConnection>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize datasource from {path}");

        await indexerClient.CreateOrUpdateDataSourceConnectionAsync(definition, cancellationToken: ct);
        logger.LogInformation("Data source '{Name}' created/updated.", name);
    }

    public async Task CreateOrUpdateSkillsetAsync(CancellationToken ct = default)
    {
        var path = Path.Combine(_settings.InfraPath, "skillsets", "jmf-enrichment-skillset.json");
        var json = await File.ReadAllTextAsync(path, ct);
        var definition = JsonSerializer.Deserialize<SearchIndexerSkillset>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize skillset from {path}");

        await indexerClient.CreateOrUpdateSkillsetAsync(definition, cancellationToken: ct);
        logger.LogInformation("Skillset '{Name}' created/updated.", definition.Name);
    }

    public async Task CreateOrUpdateIndexerAsync(string name, CancellationToken ct = default)
    {
        var path = Path.Combine(_settings.InfraPath, "indexers", $"{name}.json");
        var json = await File.ReadAllTextAsync(path, ct);
        var definition = JsonSerializer.Deserialize<SearchIndexer>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize indexer from {path}");

        await indexerClient.CreateOrUpdateIndexerAsync(definition, cancellationToken: ct);
        logger.LogInformation("Indexer '{Name}' created/updated.", name);
    }
}
