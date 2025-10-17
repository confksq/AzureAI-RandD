using Azure.Identity;
using Azure.Search.Documents.Indexes;
using JMF.AISearch.Setup.Configuration;
using JMF.AISearch.Setup.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(config =>
    {
        config.AddJsonFile("appsettings.json", optional: false);
        config.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<AzureSearchSettings>(ctx.Configuration.GetSection("AzureSearch"));

        var searchSettings = ctx.Configuration.GetSection("AzureSearch").Get<AzureSearchSettings>()!;
        var credential = new DefaultAzureCredential();
        var endpoint = new Uri(searchSettings.ServiceEndpoint);

        services.AddSingleton(new SearchIndexClient(endpoint, credential));
        services.AddSingleton(new SearchIndexerClient(endpoint, credential));
        services.AddScoped<ISearchSetupService, SearchSetupService>();
        services.AddScoped<IIndexerService, IndexerService>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .Build();

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "provision";
var logger = host.Services.GetRequiredService<ILogger<Program>>();

await using var scope = host.Services.CreateAsyncScope();
var setupService = scope.ServiceProvider.GetRequiredService<ISearchSetupService>();
var indexerService = scope.ServiceProvider.GetRequiredService<IIndexerService>();

switch (command)
{
    case "provision":
        await setupService.ProvisionAllAsync();
        break;

    case "run-indexers":
        await indexerService.RunIndexerAsync("jmf-sharepoint-indexer");
        await indexerService.RunIndexerAsync("jmf-cosmos-indexer");
        break;

    case "status":
        await indexerService.GetIndexerStatusAsync("jmf-sharepoint-indexer");
        await indexerService.GetIndexerStatusAsync("jmf-cosmos-indexer");
        break;

    case "reset":
        var indexerName = args.Length > 1 ? args[1] : "jmf-sharepoint-indexer";
        await indexerService.ResetIndexerAsync(indexerName);
        break;

    default:
        logger.LogError("Unknown command '{Command}'. Valid: provision | run-indexers | status | reset [indexer-name]", command);
        break;
}
