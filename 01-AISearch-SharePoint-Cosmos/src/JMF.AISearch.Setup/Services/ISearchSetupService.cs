namespace JMF.AISearch.Setup.Services;

public interface ISearchSetupService
{
    Task CreateOrUpdateIndexAsync(CancellationToken ct = default);
    Task CreateOrUpdateDataSourceAsync(string name, CancellationToken ct = default);
    Task CreateOrUpdateSkillsetAsync(CancellationToken ct = default);
    Task CreateOrUpdateIndexerAsync(string name, CancellationToken ct = default);
    Task ProvisionAllAsync(CancellationToken ct = default);
}
