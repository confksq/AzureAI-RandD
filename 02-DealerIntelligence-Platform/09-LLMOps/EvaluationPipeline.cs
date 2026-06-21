// ============================================================
// GAP TOPIC: LLMOps — Automated Evaluation Pipeline
// ============================================================
// WHAT: Runs your agent against a golden dataset automatically
//       Scores: groundedness, relevance, coherence, accuracy
//       Quality gate: if scores below threshold → block deployment
// WHY:  You can't manually test every prompt change
//       Automated eval catches regressions before they reach production
// JMA:  100 golden claim test cases → must score ≥ 0.85 groundedness
//       before any prompt change deploys to production
// HEALTHCARE EQUIVALENT: 100 golden prior auth cases → must score
//       ≥ 0.90 groundedness (clinical = higher bar than standard)
// ============================================================
// INTERVIEW: "How do you know a prompt change is safe to deploy?"
// "We run it through our automated eval pipeline before deployment.
//  100 golden test cases that we know the correct answers for.
//  We score groundedness, relevance, and decision accuracy.
//  If groundedness drops below 0.85, the CI pipeline fails —
//  the change doesn't deploy. This is the quality gate in LLMOps.
//  GPT-4o itself acts as the evaluator judge — it scores each
//  response against the expected answer."
// ============================================================

namespace DealerIntelligence.LLMOps;

public class EvaluationPipeline
{
    private readonly IncentiveClaimAgent _agent;
    private readonly EvaluatorLLM       _evaluator;
    private readonly ILogger<EvaluationPipeline> _logger;

    // INTERVIEW: Quality gate thresholds — what must pass for deployment to proceed
    private const double GroundednessThreshold = 0.85;
    private const double RelevanceThreshold    = 0.80;
    private const double AccuracyThreshold     = 0.90;  // decision correct vs golden answer

    public EvaluationPipeline(
        IncentiveClaimAgent agent,
        EvaluatorLLM evaluator,
        ILogger<EvaluationPipeline> logger)
    {
        _agent     = agent;
        _evaluator = evaluator;
        _logger    = logger;
    }

    /// <summary>
    /// Runs the full evaluation pipeline against the golden dataset.
    /// Called by Azure DevOps CI pipeline on every prompt change.
    /// Returns: pass/fail + detailed scores.
    /// INTERVIEW: This is what runs in CI before every deployment.
    /// </summary>
    public async Task<EvalReport> RunAsync(List<GoldenTestCase> goldenDataset)
    {
        _logger.LogInformation("[EVAL] Starting evaluation run on {Count} test cases", goldenDataset.Count);

        var scores = new List<TestCaseScore>();

        foreach (var testCase in goldenDataset)
        {
            // Run the agent on each test case
            var agentResponse = await _agent.ProcessClaimAsync(testCase.ClaimRequest);

            // INTERVIEW: GPT-4o acts as the evaluator judge
            // It compares agent output vs expected output and scores each dimension
            var score = await _evaluator.ScoreAsync(new EvalInput
            {
                Question       = testCase.Description,
                Context        = testCase.PolicyContext,    // what was retrieved
                AgentResponse  = agentResponse.Rationale,
                ExpectedAnswer = testCase.ExpectedRationale,
                AgentDecision  = agentResponse.Status,
                ExpectedDecision = testCase.ExpectedStatus
            });

            scores.Add(new TestCaseScore
            {
                TestCaseId    = testCase.Id,
                Groundedness  = score.Groundedness,
                Relevance     = score.Relevance,
                DecisionMatch = agentResponse.Status == testCase.ExpectedStatus
            });

            _logger.LogInformation(
                "[EVAL] Case {Id}: Groundedness={G:F2} Relevance={R:F2} Decision={D}",
                testCase.Id, score.Groundedness, score.Relevance,
                agentResponse.Status == testCase.ExpectedStatus ? "✓" : "✗");
        }

        var report = BuildReport(scores);

        // INTERVIEW: Quality gate — fail the CI pipeline if scores below threshold
        if (!report.PassesGate)
        {
            _logger.LogCritical(
                "[EVAL] QUALITY GATE FAILED. Groundedness: {G:F2} (need {Threshold}). Deployment blocked.",
                report.AvgGroundedness, GroundednessThreshold);
        }
        else
        {
            _logger.LogInformation("[EVAL] Quality gate PASSED. Safe to deploy.");
        }

        return report;
    }

    private EvalReport BuildReport(List<TestCaseScore> scores) => new()
    {
        RunAt            = DateTime.UtcNow,
        TotalCases       = scores.Count,
        AvgGroundedness  = scores.Average(s => s.Groundedness),
        AvgRelevance     = scores.Average(s => s.Relevance),
        DecisionAccuracy = scores.Count(s => s.DecisionMatch) / (double)scores.Count,
        PassesGate       = scores.Average(s => s.Groundedness)  >= GroundednessThreshold &&
                           scores.Average(s => s.Relevance)     >= RelevanceThreshold    &&
                           scores.Count(s => s.DecisionMatch) / (double)scores.Count >= AccuracyThreshold
    };
}

public record GoldenTestCase
{
    public string      Id               { get; init; } = string.Empty;
    public string      Description      { get; init; } = string.Empty;
    public ClaimRequest ClaimRequest    { get; init; } = new();
    public string      PolicyContext    { get; init; } = string.Empty;
    public string      ExpectedStatus   { get; init; } = string.Empty;
    public string      ExpectedRationale { get; init; } = string.Empty;
}

public record EvalReport
{
    public DateTime RunAt            { get; init; }
    public int      TotalCases       { get; init; }
    public double   AvgGroundedness  { get; init; }
    public double   AvgRelevance     { get; init; }
    public double   DecisionAccuracy { get; init; }
    public bool     PassesGate       { get; init; }
}

// Placeholder types
public record TestCaseScore { public string TestCaseId = ""; public double Groundedness; public double Relevance; public bool DecisionMatch; }
public record EvalInput { public string Question = ""; public string Context = ""; public string AgentResponse = ""; public string ExpectedAnswer = ""; public string AgentDecision = ""; public string ExpectedDecision = ""; }
public record EvalScore { public double Groundedness; public double Relevance; }
public class EvaluatorLLM { public Task<EvalScore> ScoreAsync(EvalInput i) => Task.FromResult(new EvalScore { Groundedness = 0.9, Relevance = 0.85 }); }
