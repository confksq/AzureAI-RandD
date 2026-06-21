// ============================================================
// MODULE 06: KernelFunction Plugin — Tool the Agent Calls
// ============================================================
// WHAT: A KernelFunction is a C# method the LLM can call as a tool
//       The LLM reads the [Description] and decides when to call it
// WHY:  Agent needs to verify dealer is enrolled in the incentive program
//       before processing any claim
// JMA:  Calls JMA DMS API to check dealer enrollment status
// HEALTHCARE EQUIVALENT: get_patient_eligibility — calls payer API
//       to check if patient is covered for the requested medication
// ============================================================
// INTERVIEW: "How does the agent know which tool to call?"
// "Each KernelFunction has a [Description] that the LLM reads.
//  The Stepwise Planner uses those descriptions to reason about
//  which function to call for each step of the task.
//  We never hardcode the order — the LLM decides based on context."
// ============================================================

using Microsoft.SemanticKernel;
using System.ComponentModel;
using Azure.Identity;

namespace DealerIntelligence.AgentWorkflow.Plugins;

public class DealerEligibilityPlugin
{
    private readonly HttpClient _dmsClient;

    public DealerEligibilityPlugin(IHttpClientFactory factory)
    {
        // INTERVIEW: HttpClient uses Managed Identity token for DMS API auth
        _dmsClient = factory.CreateClient("DMS");
    }

    // INTERVIEW: [KernelFunction] makes this method callable by the LLM
    // [Description] is what the LLM reads to decide when to call this function
    // Good descriptions = agent makes correct tool selection decisions
    [KernelFunction("check_dealer_eligibility")]
    [Description("Checks if a dealer is enrolled in a specific incentive program and eligible to submit claims. Call this FIRST before checking any policy or processing any claim.")]
    public async Task<DealerEligibilityResult> CheckEligibilityAsync(
        [Description("The unique dealer identifier from the JMA dealer network")] string dealerId,
        [Description("The incentive program code (e.g., TFS-Q1-2026, GULF-LOYALTY)")] string programCode)
    {
        // Call DMS API — Managed Identity handles auth automatically
        var response = await _dmsClient.GetFromJsonAsync<DmsEligibilityResponse>(
            $"/api/dealers/{dealerId}/programs/{programCode}/eligibility");

        if (response == null)
            return new DealerEligibilityResult { IsEligible = false, Reason = "DMS returned no data" };

        // INTERVIEW: Return structured data — LLM reads this in the Observe step
        // and uses it to decide the next reasoning step
        return new DealerEligibilityResult
        {
            IsEligible    = response.Enrolled && response.AccountInGoodStanding,
            DealerName    = response.DealerName,
            Region        = response.Region,
            EnrolledSince = response.EnrolledSince,
            Reason        = response.Enrolled
                ? "Dealer enrolled and account in good standing"
                : $"Not eligible: {response.IneligibilityReason}"
        };
    }
}

public record DealerEligibilityResult
{
    public bool     IsEligible    { get; init; }
    public string   DealerName    { get; init; } = string.Empty;
    public string   Region        { get; init; } = string.Empty;
    public DateTime EnrolledSince { get; init; }
    public string   Reason        { get; init; } = string.Empty;
}

// Internal DTO from DMS API
internal record DmsEligibilityResponse(
    bool     Enrolled,
    bool     AccountInGoodStanding,
    string   DealerName,
    string   Region,
    DateTime EnrolledSince,
    string   IneligibilityReason);
