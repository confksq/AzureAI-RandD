// ============================================================
// MODULE 10: Fault Tolerance — Human Escalation Service
// ============================================================
// WHAT: Routes agent failures and uncertain decisions to human reviewers
//       Generates a ticket, notifies RSM/reviewer, tracks state
// WHY:  AI agents must know their limits — when to hand off to humans
//       Escalation is not failure; it's a safety feature
// JMA:  RSM (Regional Sales Manager) reviews escalated claims
// HEALTHCARE EQUIVALENT: Clinical pharmacist reviews escalated
//       prior auth cases — especially when clinical criteria unclear
// ============================================================
// INTERVIEW: "What happens when the agent can't decide?"
// "It escalates — never guesses. We generate a structured ticket
//  with all the context the RSM needs: claim details, what the agent
//  tried, why it couldn't decide, relevant policy chunks it found.
//  RSM gets an email + Teams message with a link to review queue.
//  Agent logs the escalation ticket ID for audit trail.
//  In healthcare: escalation to a clinical pharmacist, not RSM,
//  but the pattern is identical."
// ============================================================

namespace DealerIntelligence.FaultTolerance;

public class EscalationService
{
    private readonly IEmailService    _email;
    private readonly ITeamsNotifier   _teams;
    private readonly ILogger<EscalationService> _logger;

    public EscalationService(
        IEmailService email,
        ITeamsNotifier teams,
        ILogger<EscalationService> logger)
    {
        _email  = email;
        _teams  = teams;
        _logger = logger;
    }

    /// <summary>
    /// Escalates a claim to the RSM (Regional Sales Manager) for human review.
    /// Called when: circuit open, fraud risk high, policy ambiguous, tool fails after retry.
    /// HEALTHCARE: Called when prior auth criteria unclear for clinical review.
    /// </summary>
    public async Task<EscalationTicket> EscalateToRSMAsync(EscalationRequest request)
    {
        var ticket = new EscalationTicket
        {
            TicketId     = $"ESC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}",
            ClaimId      = request.ClaimId,
            Reason       = request.Reason,
            Priority     = DeterminePriority(request),
            CreatedAt    = DateTime.UtcNow,
            ReviewerEmail = request.RSMEmail
        };

        _logger.LogWarning(
            "[ESCALATION] Ticket {TicketId} created | Claim: {ClaimId} | Reason: {Reason} | Priority: {Priority}",
            ticket.TicketId, ticket.ClaimId, ticket.Reason, ticket.Priority);

        // Notify RSM via email + Teams
        // INTERVIEW: Human receives full context — they shouldn't need to re-investigate
        // Package: claim details + what agent tried + why it escalated + relevant policy chunks
        var notificationBody = FormatNotification(ticket, request);
        await Task.WhenAll(
            _email.SendAsync(request.RSMEmail, $"[Action Required] Claim {ticket.ClaimId} needs review", notificationBody),
            _teams.PostAsync($"channel-rsm-{request.RSMRegion}", notificationBody)
        );

        return ticket;
    }

    private string DeterminePriority(EscalationRequest req) =>
        req.ClaimAmount > 50_000 ? "HIGH" :
        req.Reason.Contains("fraud", StringComparison.OrdinalIgnoreCase) ? "HIGH" : "NORMAL";

    private string FormatNotification(EscalationTicket ticket, EscalationRequest req) => $"""
        **Claim Escalation — Action Required**

        Ticket: {ticket.TicketId}
        Claim ID: {req.ClaimId}
        Dealer: {req.DealerId}
        Program: {req.ProgramCode}
        Amount: ${req.ClaimAmount:N0}
        Priority: {ticket.Priority}

        **Why escalated:** {req.Reason}

        **Agent findings:**
        {req.AgentSummary}

        **Relevant policy context:**
        {req.PolicyContext}

        Please review in the dealer portal: https://portal.jmfamily.com/claims/{req.ClaimId}
        """;
}

public record EscalationRequest
{
    public string ClaimId      { get; init; } = string.Empty;
    public string DealerId     { get; init; } = string.Empty;
    public string ProgramCode  { get; init; } = string.Empty;
    public decimal ClaimAmount { get; init; }
    public string Reason       { get; init; } = string.Empty;
    public string AgentSummary { get; init; } = string.Empty;
    public string PolicyContext { get; init; } = string.Empty;
    public string RSMEmail     { get; init; } = string.Empty;
    public string RSMRegion    { get; init; } = string.Empty;
}

public record EscalationTicket
{
    public string   TicketId     { get; init; } = string.Empty;
    public string   ClaimId      { get; init; } = string.Empty;
    public string   Reason       { get; init; } = string.Empty;
    public string   Priority     { get; init; } = string.Empty;
    public DateTime CreatedAt    { get; init; }
    public string   ReviewerEmail { get; init; } = string.Empty;
}

public interface IEmailService  { Task SendAsync(string to, string subject, string body); }
public interface ITeamsNotifier { Task PostAsync(string channel, string message); }
