// ============================================================
// MODULE 10: Fault Tolerance — Retry with Exponential Backoff
// ============================================================
// WHAT: Automatically retries failed operations with increasing delays
//       Exponential backoff = wait 1s, then 2s, then 4s between retries
// WHY:  External APIs (DMS, payer APIs) have transient failures
//       Immediate retry = hammers the failing service
//       Exponential backoff = gives the service time to recover
// JMA:  DMS API times out → retry 3x with backoff before escalating
// HEALTHCARE EQUIVALENT: Payer eligibility API fails → retry before
//       routing prior auth to human reviewer
// ============================================================
// INTERVIEW: "How do you handle tool call failures in your agent?"
// "Retry once with exponential backoff. If still failing after max
//  retries, we escalate to human review — the agent never guesses.
//  In a clinical workflow, a delayed decision is always better than
//  a wrong one. We use Polly for resilience policies in .NET."
// ============================================================

using Polly;
using Polly.Retry;

namespace DealerIntelligence.FaultTolerance;

public class AgentRetryPolicy
{
    // INTERVIEW: Polly is the standard .NET resilience library
    // Retry pipelines are composable — retry + circuit breaker + timeout
    private readonly ResiliencePipeline _pipeline;

    public AgentRetryPolicy(ILogger<AgentRetryPolicy> logger)
    {
        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                // INTERVIEW: 3 retries = reasonable for transient API failures
                // More than 3 = you're probably hitting a real outage, not transient
                MaxRetryAttempts = 3,

                // INTERVIEW: Exponential backoff with jitter
                // Jitter = small random delay added to prevent thundering herd
                // If 100 agents all retry at exactly 2s → spike on recovering service
                // Jitter spreads them out: 2s + random(0-500ms)
                Delay            = TimeSpan.FromSeconds(1),
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,

                // INTERVIEW: Only retry on transient errors — not on business logic failures
                // HttpRequestException = transient (network issue) → RETRY
                // ArgumentException = programming error → DON'T RETRY
                ShouldHandle     = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutException>(),

                OnRetry = args =>
                {
                    logger.LogWarning(
                        "[RETRY] Attempt {Attempt} failed: {Error}. Retrying in {Delay}ms...",
                        args.AttemptNumber + 1,
                        args.Outcome.Exception?.Message,
                        args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    /// Executes an operation with retry protection.
    /// INTERVIEW: Wrap every external API call in this — DMS, payer APIs, search
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        return await _pipeline.ExecuteAsync(operation, ct);
    }
}
