namespace JMF.AISearch.Setup.Configuration;

public sealed class AzureSearchSettings
{
    public string ServiceEndpoint { get; init; } = string.Empty;
    public string IndexName       { get; init; } = "jmf-documents";
    public string OpenAIEndpoint  { get; init; } = string.Empty;
    public string InfraPath       { get; init; } = "infra";
}
