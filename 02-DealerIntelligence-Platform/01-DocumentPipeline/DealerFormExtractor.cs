// ============================================================
// MODULE 09: OCR / Document Intelligence
// ============================================================
// WHAT: Extracts structured data from dealer incentive claim forms
//       Uses Azure Document Intelligence (formerly Form Recognizer)
//       Routes by confidence: auto-approve, human review, dead letter
// WHY:  Dealers submit handwritten/scanned claim forms as PDFs
//       DI converts them to machine-readable structured data
//       Confidence routing = don't pass low-confidence extractions to agent
// JMA:  Claim forms → DI → structured ClaimRequest → IncentiveClaimAgent
// HEALTHCARE EQUIVALENT: Prior auth PDF forms → DI → structured
//       PriorAuthRequest → ClinicalReviewAgent
//       PA forms often hand-annotated = confidence routing critical
// ============================================================
// INTERVIEW: "How do you get PDF claim forms into your agent?"
// "Azure Document Intelligence with a custom model trained on JMA's
//  claim forms. We extract: dealer ID, VIN, program code, amounts, dates.
//  DI returns a confidence score per field. If overall > 0.90 → process
//  automatically. 0.70-0.90 → route to data entry for manual verification.
//  Below 0.70 → dead letter queue — form too poor quality to process.
//  That confidence routing is critical — you can't send a 0.4 confidence
//  VIN to the claim agent and expect a good decision."
// ============================================================

using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;

namespace DealerIntelligence.DocumentPipeline;

public class DealerFormExtractor
{
    private readonly DocumentAnalysisClient _diClient;
    private readonly ILogger<DealerFormExtractor> _logger;

    // INTERVIEW: Confidence routing thresholds
    private const double AutoProcessThreshold = 0.90;   // >= 0.90 → agent processes automatically
    private const double ReviewThreshold      = 0.70;   // 0.70-0.89 → human verification queue
                                                         // < 0.70 → dead letter

    public DealerFormExtractor(string diEndpoint, ILogger<DealerFormExtractor> logger)
    {
        // INTERVIEW: DefaultAzureCredential = Managed Identity in production
        // Eliminates stored secrets — identity is the credential
        _diClient = new DocumentAnalysisClient(
            new Uri(diEndpoint),
            new DefaultAzureCredential());
        _logger = logger;
    }

    /// <summary>
    /// Extracts structured claim data from a dealer incentive claim PDF.
    /// Routes by extraction confidence to ensure data quality before agent processing.
    /// HEALTHCARE: Same pattern for prior auth forms and Explanation of Benefits PDFs.
    /// </summary>
    public async Task<ExtractionResult> ExtractAsync(Stream pdfStream, string documentId)
    {
        _logger.LogInformation("[DI] Starting extraction for document {DocId}", documentId);

        // INTERVIEW: "jma-incentive-claim" = custom model trained on JMA's forms
        // Custom model = 5-10 sample forms labeled in Document Intelligence Studio
        // Learns: where dealer ID is, where VIN is, where amount is on JMA's specific forms
        var operation = await _diClient.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            modelId: "jma-incentive-claim",  // custom model ID
            document: pdfStream);

        var result   = operation.Value;
        var document = result.Documents.FirstOrDefault();

        if (document == null)
        {
            _logger.LogError("[DI] No document found in extraction result for {DocId}", documentId);
            return ExtractionResult.DeadLetter(documentId, "Document Intelligence returned no document");
        }

        // INTERVIEW: Extract each field with its confidence score
        // Never assume a field was extracted correctly — always check confidence
        var fields = new Dictionary<string, (string Value, double Confidence)>();

        foreach (var (fieldName, field) in document.Fields)
        {
            var value      = field.Content ?? string.Empty;
            var confidence = field.Confidence ?? 0.0;
            fields[fieldName] = (value, confidence);

            _logger.LogDebug("[DI] Field {Field}: '{Value}' (confidence: {Conf:P0})",
                fieldName, value, confidence);
        }

        // INTERVIEW: Overall confidence = minimum confidence across required fields
        // One low-confidence field = the whole extraction is unreliable
        var requiredFields  = new[] { "DealerId", "VehicleVin", "ProgramCode", "ClaimAmount", "SaleDate" };
        var minConfidence   = requiredFields
            .Where(f => fields.ContainsKey(f))
            .Select(f => fields[f].Confidence)
            .DefaultIfEmpty(0.0)
            .Min();

        _logger.LogInformation("[DI] Document {DocId}: min confidence = {Conf:P0}, routing...",
            documentId, minConfidence);

        // INTERVIEW: This is confidence-based routing
        if (minConfidence >= AutoProcessThreshold)
        {
            // High confidence → build ClaimRequest, send to agent pipeline
            return ExtractionResult.AutoProcess(documentId, BuildClaimRequest(fields), minConfidence);
        }
        else if (minConfidence >= ReviewThreshold)
        {
            // Medium confidence → human verifies before agent processes
            return ExtractionResult.NeedsReview(documentId, BuildClaimRequest(fields), minConfidence);
        }
        else
        {
            // Low confidence → dead letter, alert ops, request re-submission
            _logger.LogWarning("[DI] Dead letter: document {DocId} confidence {Conf:P0} too low",
                documentId, minConfidence);
            return ExtractionResult.DeadLetter(documentId, $"Extraction confidence {minConfidence:P0} below minimum threshold");
        }
    }

    private static ClaimRequest BuildClaimRequest(Dictionary<string, (string Value, double Confidence)> fields)
    {
        bool TryGet(string key, out string val)
        {
            val = fields.TryGetValue(key, out var f) ? f.Value : string.Empty;
            return !string.IsNullOrEmpty(val);
        }

        TryGet("DealerId",    out var dealerId);
        TryGet("VehicleVin",  out var vin);
        TryGet("ProgramCode", out var program);
        TryGet("ClaimAmount", out var amountStr);
        TryGet("SaleDate",    out var dateStr);

        return new ClaimRequest
        {
            ClaimId     = Guid.NewGuid().ToString(),
            DealerId    = dealerId,
            VehicleVin  = vin,
            ProgramCode = program,
            ClaimAmount = decimal.TryParse(amountStr, out var amt) ? amt : 0,
            SaleDate    = DateTime.TryParse(dateStr, out var date) ? date : DateTime.MinValue
        };
    }
}

public record ExtractionResult
{
    public string       DocumentId  { get; init; } = string.Empty;
    public string       Route       { get; init; } = string.Empty;   // "auto" | "review" | "dead_letter"
    public ClaimRequest? Claim      { get; init; }
    public double       Confidence  { get; init; }
    public string       ErrorReason { get; init; } = string.Empty;

    public static ExtractionResult AutoProcess(string id, ClaimRequest claim, double conf) =>
        new() { DocumentId = id, Route = "auto", Claim = claim, Confidence = conf };

    public static ExtractionResult NeedsReview(string id, ClaimRequest claim, double conf) =>
        new() { DocumentId = id, Route = "review", Claim = claim, Confidence = conf };

    public static ExtractionResult DeadLetter(string id, string reason) =>
        new() { DocumentId = id, Route = "dead_letter", ErrorReason = reason, Confidence = 0.0 };
}
