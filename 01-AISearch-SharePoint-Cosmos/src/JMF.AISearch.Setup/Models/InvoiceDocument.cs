using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace JMF.AISearch.Setup.Models;

public sealed class InvoiceDocument
{
    [SimpleField(IsKey = true, IsFilterable = true)]
    public string Id { get; set; } = string.Empty;

    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.EnMicrosoft)]
    public string? Content { get; set; }

    [SearchableField(IsFilterable = true, IsSortable = true)]
    public string? Title { get; set; }

    [SimpleField(IsRetrievable = true)]
    public string? Url { get; set; }

    [SimpleField(IsFilterable = true)]
    public string? Source { get; set; }

    [SimpleField(IsFilterable = true)]
    public string? DocumentType { get; set; }

    [SearchableField(IsFilterable = true)]
    public string? DealerCode { get; set; }

    [SearchableField(IsFilterable = true)]
    public string? DealerName { get; set; }

    [SimpleField(IsFilterable = true, IsSortable = true)]
    public double? Amount { get; set; }

    [SimpleField(IsFilterable = true)]
    public string? Status { get; set; }

    [SimpleField(IsFilterable = true, IsSortable = true)]
    public DateTimeOffset? CreatedDate { get; set; }

    [SimpleField(IsFilterable = true, IsSortable = true)]
    public DateTimeOffset? ModifiedDate { get; set; }

    [SearchableField(IsFilterable = true)]
    public IList<string>? KeyPhrases { get; set; }

    [SearchableField(IsFilterable = true)]
    public IList<string>? Entities { get; set; }

    [SimpleField(IsFilterable = true)]
    public string? Language { get; set; }

    [VectorSearchField(VectorSearchDimensions = 1536, VectorSearchProfileName = "jmf-vector-profile")]
    public IReadOnlyList<float>? ContentVector { get; set; }
}
