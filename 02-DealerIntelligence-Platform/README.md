# JMA Dealer Intelligence Platform

> **Your domain. Your terms. Same architecture as VitalCare.**
> Use this project to understand concepts — then map to healthcare for the Ascendion interview.

---

## What This Project Does

End-to-end multi-agent AI system that processes dealer incentive claims:

```
Dealer submits incentive claim form (PDF)
        ↓
[01-DocumentPipeline]   Document Intelligence extracts structured data + chunks policy docs
        ↓
[02-RAGSearch]          Hybrid retrieval (HNSW vector + BM25 keyword + RRF) from AI Search
        ↓
[03-AgentWorkflow]      Incentive Claim Agent: SK plugins + ReAct loop + audit filter
        ↓
[04-MetaAgentOrchestration]  Supervisor delegates to ClaimValidator + PolicyChecker + FraudDetector
        ↓
[05-A2ACommunication]   Typed A2A messages between agents with schema validation + audit log
        ↓
[06-MCPHub]             MCP Hub routes tool calls → APIM gateway → backend DMS APIs
        ↓
[07-FaultTolerance]     DMS API fails → retry → circuit breaker → escalate to RSM
        ↓
[08-PromptEngineering]  System prompts + few-shot examples + token optimization + streaming
        ↓
[09-LLMOps]             Eval pipeline + groundedness monitoring + prompt versioning + CI/CD
```

---

## Domain Mapping — JMA to Healthcare (Ascendion Interview)

| JMA Term | Healthcare Equivalent | Interview Context |
|---|---|---|
| Dealer | Patient | Entity requesting approval |
| Incentive Claim | Prior Authorization | Request for approval |
| Dealer Management System (DMS) | EHR / FHIR | Source of truth data |
| JMA Incentive Policy API | Payer Eligibility API | Rules for approval |
| Dealer Policy Documents (SharePoint) | Formulary / Clinical Guidelines | Policy knowledge base |
| Vehicle Inspection Report | Prescription / Clinical Note | Document to extract |
| Regional Sales Manager (RSM) | Physician Reviewer | Human escalation |
| SOC 2 / Dealer PII | HIPAA / PHI | Compliance layer |
| Toyota Program Code | ICD-10 / Drug Code | Classification identifier |

---

## Module Coverage Map

| Folder | Interview Module | Concept Covered |
|---|---|---|
| `01-DocumentPipeline/` | Module 09 | OCR — Document Intelligence, confidence routing, chunking strategies |
| `02-RAGSearch/` | Gap | HNSW indexing, cosine similarity, hybrid retrieval, RRF fusion |
| `03-AgentWorkflow/` | Module 06 | End-to-end agent — SK plugins, ReAct loop, FunctionInvocationFilter |
| `04-MetaAgentOrchestration/` | Module 07 | Meta-agents — supervisor + specialist sub-agents, failure propagation |
| `05-A2ACommunication/` | Module 08 | A2A Protocol — typed messages, schema validation, audit logging |
| `06-MCPHub/` | Module 05 | MCP Hub + APIM hybrid — tool discovery, routing, enterprise governance |
| `07-FaultTolerance/` | Module 10 | Fault tolerance — retry, circuit breaker, self-healing, escalation |
| `08-PromptEngineering/` | Gap | System prompts, few-shot, CoT, token optimization, streaming |
| `09-LLMOps/` | Gap + M11 | Eval pipeline, groundedness drift, prompt versioning, CI/CD |

---

## Tech Stack

```
Language:        C# / .NET 8
Orchestration:   Semantic Kernel (Microsoft)
LLM:             Azure OpenAI (GPT-4o)
Embeddings:      Azure OpenAI (text-embedding-3-large)
Vector Search:   Azure AI Search (HNSW, hybrid retrieval)
Document OCR:    Azure Document Intelligence
Security:        Azure Content Safety + Managed Identity (DefaultAzureCredential)
Monitoring:      Azure Monitor + Application Insights
LLMOps:          Azure AI Foundry + Azure DevOps CI/CD
```

---

## How to Read This Project

Every `.cs` file has:
- **Header block** — which module it covers and why
- **`// INTERVIEW:`** comments — the exact lines to memorize for the interview
- **`// WHY:`** comments — the reasoning behind each decision
- **`// JMA:`** comments — anchors to JMA production context
- **`// HEALTHCARE EQUIVALENT:`** comments — maps to Ascendion interview context

---

## The Interview Bridge (Say This)

> *"The architecture I designed for VitalCare is the same pattern I've been running in production at JM Family — just different domain. At JM Family, dealers submit incentive claims. Our agent validates against JMA policy documents in Azure AI Search, calls the DMS to verify dealer eligibility and sales data, and writes the approval decision. For VitalCare I applied the same pattern — patients submit prior auth requests, the agent validates against payer formulary in AI Search, calls the FHIR API, writes the auth decision. Same supervisor-to-specialist agent hierarchy, same HNSW RAG pipeline, same fault tolerance with auto-escalation. The healthcare layer adds PHI handling and HIPAA audit logging on top."*

---

## Project Structure

```
02-DealerIntelligence-Platform/
├── README.md                                  ← You are here — read this first
├── 01-DocumentPipeline/
│   ├── DealerFormExtractor.cs                 ← Module 09: Document Intelligence OCR + confidence routing
│   └── ChunkingStrategy.cs                    ← Gap: Fixed, paragraph, semantic, parent-child chunking
├── 02-RAGSearch/
│   ├── HNSWVectorSearch.cs                    ← Gap: HNSW index config, cosine similarity
│   └── HybridRetrieval.cs                     ← Gap: Keyword + vector + RRF fusion
├── 03-AgentWorkflow/
│   ├── IncentiveClaimAgent.cs                 ← Module 06: Full SK agent with ReAct loop
│   ├── AuditFilter.cs                         ← Module 06: FunctionInvocationFilter (HIPAA audit trail)
│   └── Plugins/
│       ├── DealerEligibilityPlugin.cs         ← KernelFunction: check dealer coverage
│       ├── PolicyLookupPlugin.cs              ← KernelFunction: RAG policy retrieval
│       └── ClaimDecisionPlugin.cs             ← KernelFunction: write decision to DMS
├── 04-MetaAgentOrchestration/
│   ├── SupervisorAgent.cs                     ← Module 07: Supervisor delegates, tracks failures
│   ├── ClaimValidatorAgent.cs                 ← Sub-agent: validates claim data completeness
│   ├── PolicyCheckerAgent.cs                  ← Sub-agent: checks incentive policy compliance
│   └── FraudDetectorAgent.cs                  ← Sub-agent: flags anomalous claim patterns
├── 05-A2ACommunication/
│   ├── AgentMessage.cs                        ← Module 08: Typed A2A message schema
│   └── AgentBus.cs                            ← Module 08: Message routing + audit log
├── 06-MCPHub/
│   ├── MCPToolRegistry.cs                     ← Module 05: Tool discovery + registration
│   └── APIMGateway.cs                         ← Module 05: Hybrid MCP + APIM pattern
├── 07-FaultTolerance/
│   ├── RetryPolicy.cs                         ← Module 10: Exponential backoff retry
│   ├── CircuitBreaker.cs                      ← Module 10: Circuit breaker pattern
│   └── EscalationService.cs                   ← Module 10: Auto-escalate to RSM / human
├── 08-PromptEngineering/
│   ├── SystemPrompts.cs                       ← Gap: System prompt templates for each agent
│   └── TokenOptimizer.cs                      ← Gap: Streaming, compression, model tier selection
└── 09-LLMOps/
    ├── EvaluationPipeline.cs                  ← Gap: Automated groundedness evaluation
    ├── GroundednessMonitor.cs                 ← Gap: Drift detection + auto-rollback trigger
    └── PromptVersioning.cs                    ← Gap: Git-based prompt versioning
```
