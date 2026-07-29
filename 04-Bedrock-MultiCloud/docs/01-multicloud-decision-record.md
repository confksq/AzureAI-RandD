# Architecture Decision Record — Multi-Cloud AI Platform

**Status:** Accepted
**Date:** 2026-02-11
**Deciders:** Platform AI working group

## Context

The platform standardized on Azure AI Foundry in 2024. Two pressures now argue
for a second inference provider:

1. **Concentration risk.** A regional capacity event on a single provider takes
   every AI-backed workflow down at once. Several are in the customer-facing
   path.
2. **Acquisition.** The 2025 acquisition brought a business unit already running
   production workloads on Amazon Bedrock. Forcing a migration would burn a
   quarter of engineering time for no user-visible benefit.

## Decision

Adopt a **two-provider posture**: Azure AI Foundry remains the primary plane;
Amazon Bedrock becomes a supported secondary. We do not adopt a third.

Workload placement is decided by data gravity, not by model benchmark scores:
inference runs in whichever cloud already holds the data. Moving a 40 GB
document corpus across clouds to reach a marginally better model is a bad trade
once egress and latency are counted.

## Provider abstraction

We deliberately do **not** build a universal provider-abstraction layer. Prior
attempts converged on the lowest common denominator and blocked adoption of
provider-specific features — Azure's evaluation harness, Bedrock's Guardrails —
that were the reason for choosing each provider in the first place.

Instead, each workload targets one provider directly, and a thin routing shim
handles failover for the three workflows that genuinely require it. The shim
translates request shape only; it does not attempt to normalize model behavior.

## Consequences

- Prompt portfolios must be maintained per provider. A prompt tuned on one
  model family does not transfer without re-evaluation.
- Evaluation must run against both providers before any model version change.
- Two IAM models, two audit trails, two cost dashboards. Accepted cost.
- Engineers need working knowledge of both SDKs. Budgeted as onboarding.

## Rejected alternatives

**Single provider with multi-region failover.** Cheaper operationally, but does
not address provider-level outages or the acquired business unit's existing
workloads.

**Self-hosted open-weight models.** Removes provider dependency but transfers
the entire reliability, scaling, and safety burden in-house. Revisit when the
team has dedicated ML-infrastructure headcount.
