// ============================================================
// MODULE 10: Fault Tolerance — Circuit Breaker Pattern
// ============================================================
// WHAT: Stops calling a failing service after too many errors
//       "Opens the circuit" → fast-fails for a cooldown period
//       Then "half-opens" → tests if service recovered
// WHY:  If DMS is down, retrying 1000 times just wastes resources
//       Circuit breaker stops the bleeding, gives DMS time to recover
// JMA:  DMS API fails 5x in 30s → circuit opens → fast-fail for 60s
//       → half-open → test one call → if success, circuit closes
// HEALTHCARE EQUIVALENT: Payer API is down → circuit opens →
//       all eligibility checks fast-fail → escalate to human
//       → circuit half-opens after 60s → tests recovery
// ============================================================
// INTERVIEW: "What's the difference between retry and circuit breaker?"
// "Retry handles individual transient failures — try again a few times.
//  Circuit breaker handles sustained outages — stop trying altogether
//  after threshold is exceeded, fast-fail for a cooldown period,
//  then probe for recovery. They work together: retry first,
//  circuit breaker if retry exhausts. In healthcare, circuit breaker
//  is critical — you don't want 1000 prior auth requests hammering
//  a payer API that's already down."
// ============================================================

using Polly;
using Polly.CircuitBreaker;

namespace DealerIntelligence.FaultTolerance;

public class DMSCircuitBreaker
{
    private readonly ResiliencePipeline _pipeline;
    private bool _isOpen = false;

    public DMSCircuitBreaker(ILogger<DMSCircuitBreaker> logger)
    {
        _pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                // INTERVIEW: Open circuit after 5 failures in a 30-second window
                FailureRatio          = 0.5,       // 50% failure rate triggers open
                MinimumThroughput     = 5,          // Need at least 5 calls to evaluate
                SamplingDuration      = TimeSpan.FromSeconds(30),

                // INTERVIEW: Stay open for 60 seconds — give DMS time to recover
                BreakDuration         = TimeSpan.FromSeconds(60),

                ShouldHandle          = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>(),

                // INTERVIEW: These callbacks let you take action on state transitions
                OnOpened = args =>
                {
                    _isOpen = true;
                    logger.LogCritical(
                        "[CIRCUIT BREAKER] DMS circuit OPENED — fast-failing all requests for {Duration}s. " +
                        "All claims will be escalated to RSM until circuit recovers.",
                        args.BreakDuration.TotalSeconds);
                    // Production: page on-call, update status page, notify RSMs
                    return ValueTask.CompletedTask;
                },

                OnClosed = args =>
                {
                    _isOpen = false;
                    logger.LogInformation("[CIRCUIT BREAKER] DMS circuit CLOSED — service recovered, resuming normal operation.");
                    return ValueTask.CompletedTask;
                },

                OnHalfOpened = args =>
                {
                    logger.LogInformation("[CIRCUIT BREAKER] DMS circuit HALF-OPEN — probing recovery with single test call.");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public bool IsOpen => _isOpen;

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        try
        {
            return await _pipeline.ExecuteAsync(operation, ct);
        }
        catch (BrokenCircuitException)
        {
            // INTERVIEW: BrokenCircuitException = circuit is open, fast-fail immediately
            // Don't retry — the circuit breaker already decided the service is down
            throw new ServiceUnavailableException("DMS API circuit is open — service temporarily unavailable. Claim escalated to RSM.");
        }
    }
}

public class ServiceUnavailableException(string message) : Exception(message);
