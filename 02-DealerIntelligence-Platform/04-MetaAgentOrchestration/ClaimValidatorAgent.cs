// ============================================================
// MODULE 07: Specialist Sub-Agent — Claim Validator
// ============================================================
// Validates claim data completeness and business rule compliance
// HEALTHCARE EQUIVALENT: EligibilityValidatorAgent
// ============================================================

namespace DealerIntelligence.MetaAgentOrchestration;

public class ClaimValidatorAgent
{
    private readonly ILogger<ClaimValidatorAgent> _logger;
    public ClaimValidatorAgent(ILogger<ClaimValidatorAgent> logger) => _logger = logger;

    public async Task<ClaimValidationResult> ValidateAsync(ClaimRequest request)
    {
        _logger.LogInformation("[VALIDATOR] Validating claim {ClaimId}", request.ClaimId);

        // INTERVIEW: Validation is fast + cheap — run FIRST before expensive LLM calls
        var failures = new List<string>();

        if (string.IsNullOrEmpty(request.DealerId))     failures.Add("Missing DealerId");
        if (string.IsNullOrEmpty(request.VehicleVin))   failures.Add("Missing VehicleVin");
        if (string.IsNullOrEmpty(request.ProgramCode))  failures.Add("Missing ProgramCode");
        if (request.ClaimAmount <= 0)                   failures.Add("Invalid claim amount");
        if (request.SaleDate > DateTime.UtcNow)         failures.Add("Sale date in future");
        if (request.VehicleVin.Length != 17)            failures.Add("VIN must be 17 characters");

        return await Task.FromResult(new ClaimValidationResult
        {
            ClaimId       = request.ClaimId,
            IsValid       = failures.Count == 0,
            FailureReason = string.Join("; ", failures),
            Confidence    = 1.0  // Rule-based = always 100% confidence
        });
    }
}

public record ClaimValidationResult
{
    public string ClaimId       { get; init; } = string.Empty;
    public bool   IsValid       { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public double Confidence    { get; init; } = 1.0;
}
