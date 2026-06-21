// ============================================================
// GAP TOPIC: LLMOps — Prompt Versioning and CI/CD Pipeline
// ============================================================
// WHAT: Treats prompts as first-class code — versioned in Git,
//       tested in CI before deploy, rolled back on regression
// WHY:  A prompt is as important as application code
//       Changing a prompt changes agent behavior — it needs a PR,
//       review, automated tests, and staged rollout, just like code
// JMA:  Prompts stored in YAML → committed to Git → CI eval runs
//       → if groundedness drops → PR blocked → human reviews
// HEALTHCARE EQUIVALENT: Clinical prompts require even stricter control
//       PHI-masking prompt, denial reasoning prompt — any change
//       must pass clinical validation before reaching production
// ============================================================
// INTERVIEW: "How do you manage prompt changes in production?"
// "Prompts live in Git alongside code — YAML files, versioned,
//  with changelogs. Every PR that touches a prompt triggers our
//  eval pipeline automatically in CI. If groundedness drops below
//  0.85, the PR is blocked. We also do canary rollouts — 5% traffic
//  to the new prompt, measure live groundedness, then promote.
//  In healthcare, we'd add clinical reviewer sign-off as a required
//  approval step before merge."
// ============================================================

using System.Text.Json;

namespace DealerIntelligence.LLMOps;

/// <summary>
/// Loads versioned prompts from Git-tracked YAML/JSON files.
/// INTERVIEW: Prompts are NOT hardcoded in code — they live in version-controlled files.
/// This means you can: see the history of every change, diff prompt changes in PR,
/// roll back a bad prompt change without deploying new code.
/// </summary>
public class PromptVersionStore
{
    private readonly string _promptsBasePath;
    private readonly ILogger<PromptVersionStore> _logger;
    private readonly Dictionary<string, PromptVersion> _cache = new();

    public PromptVersionStore(string promptsBasePath, ILogger<PromptVersionStore> logger)
    {
        _promptsBasePath = promptsBasePath;
        _logger          = logger;
    }

    /// <summary>
    /// Loads a prompt by name and version.
    /// INTERVIEW: "latest" in dev/staging; pinned version in production.
    /// Pinning = production never auto-picks up a new prompt until explicitly promoted.
    /// </summary>
    public async Task<PromptVersion> LoadAsync(string promptName, string version = "latest")
    {
        var cacheKey = $"{promptName}:{version}";

        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var filePath = version == "latest"
            ? Path.Combine(_promptsBasePath, $"{promptName}.json")
            : Path.Combine(_promptsBasePath, "versions", $"{promptName}.v{version}.json");

        _logger.LogInformation("[PROMPT STORE] Loading {PromptName} v{Version} from {Path}",
            promptName, version, filePath);

        var json   = await File.ReadAllTextAsync(filePath);
        var prompt = JsonSerializer.Deserialize<PromptVersion>(json)!;
        _cache[cacheKey] = prompt;
        return prompt;
    }

    /// <summary>
    /// Validates a candidate prompt against golden test cases before promotion.
    /// Called by CI pipeline — returns false → PR is blocked.
    /// </summary>
    public async Task<bool> ValidateBeforePromotionAsync(
        PromptVersion candidate,
        List<GoldenTestCase> goldenCases,
        double groundednessThreshold = 0.85)
    {
        _logger.LogInformation(
            "[PROMPT VALIDATE] Testing prompt {Name} v{Version} against {Count} golden cases",
            candidate.Name, candidate.Version, goldenCases.Count);

        // Run eval (simplified — real impl calls EvaluationPipeline)
        var simulatedGroundedness = 0.88;

        var passes = simulatedGroundedness >= groundednessThreshold;
        _logger.LogInformation(
            "[PROMPT VALIDATE] {Name} v{Version}: groundedness={Score:F2} → {Result}",
            candidate.Name, candidate.Version, simulatedGroundedness,
            passes ? "PASS ✓" : "FAIL ✗ — DEPLOYMENT BLOCKED");

        return await Task.FromResult(passes);
    }
}

public record PromptVersion
{
    public string   Name        { get; init; } = string.Empty;
    public string   Version     { get; init; } = string.Empty;
    public string   Content     { get; init; } = string.Empty;
    public DateTime CreatedAt   { get; init; }
    public string   Author      { get; init; } = string.Empty;
    public string   ChangeLog   { get; init; } = string.Empty;   // INTERVIEW: WHY was it changed?
    public string   ApprovedBy  { get; init; } = string.Empty;   // INTERVIEW: Who signed off?
}

// ============================================================
// AZURE DEVOPS CI PIPELINE YAML (conceptual reference)
// ============================================================
// INTERVIEW: "Walk me through your LLMOps CI/CD pipeline"
// "Every PR that changes a prompt or agent code triggers this pipeline:
//  1. Build .NET solution
//  2. Run unit tests
//  3. Run prompt eval pipeline against 100 golden test cases
//  4. Quality gate: groundedness ≥ 0.85, accuracy ≥ 0.90
//  5. If gate passes → approve for merge
//  6. After merge → deploy to staging → canary 5% → promote"
//
// # azure-pipelines-llmops.yml (conceptual)
// trigger:
//   paths:
//     include:
//       - src/08-PromptEngineering/**
//       - src/09-LLMOps/**
//
// stages:
//   - stage: Evaluate
//     jobs:
//       - job: PromptEvaluation
//         steps:
//           - script: dotnet test --filter Category=Eval
//           - script: dotnet run --project EvalRunner -- --threshold 0.85
//           - task: PublishTestResults@2
//
//   - stage: QualityGate
//     dependsOn: Evaluate
//     condition: succeeded()
//     jobs:
//       - job: Gate
//         steps:
//           - script: ./scripts/check-eval-scores.sh --groundedness 0.85
