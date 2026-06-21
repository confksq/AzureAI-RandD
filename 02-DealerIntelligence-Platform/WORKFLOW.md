# JMA Dealer Intelligence Platform — End-to-End Workflow

---

## PATH A: ONE-TIME SETUP (Index Policy Documents)

```
Policy PDFs (Incentive Program Docs)
           |
           v
  ┌─────────────────────────────────────┐
  │   01-DocumentPipeline               │  MODULE 09
  │   ChunkingStrategy.cs               │
  │                                     │
  │   Strategy decision:                │
  │   ┌──────────┬──────────────────┐  │
  │   │ fixed    │ homogeneous text │  │
  │   │ para     │ policy rules     │  │  ← PREFERRED for policy PDFs
  │   │ parent-  │ RAG quality      │  │  ← child = embed; parent = inject
  │   │  child   │ (best)           │  │
  │   └──────────┴──────────────────┘  │
  └─────────────────────────────────────┘
           |
           | chunks (200-300 tokens each)
           v
  ┌────────────────────────────────┐
  │  Azure OpenAI                  │  GAP TOPIC
  │  text-embedding-3-large        │
  │  → float[1536] per chunk       │
  └────────────────────────────────┘
           |
           | embeddings
           v
  ┌────────────────────────────────────────┐
  │  02-RAGSearch                          │  GAP TOPIC
  │  HNSWVectorSearch.cs                   │
  │                                        │
  │  Azure AI Search Index                 │
  │  ┌────────────────────────────────┐   │
  │  │ HNSW Algorithm Config          │   │
  │  │  m=4 (connections per node)    │   │
  │  │  efConstruction=400 (accuracy) │   │
  │  │  metric=cosine similarity      │   │
  │  └────────────────────────────────┘   │
  │  + BM25 keyword index (auto)          │
  │  + Semantic re-ranker config          │
  └────────────────────────────────────────┘
           |
           | Index ready — used at query time
           v
      [ INDEX READY ]  ✓
```

---

## PATH B: RUNTIME (Process Dealer Claim)

```
Dealer submits incentive claim PDF
           |
           v
┌──────────────────────────────────────────────────────────────┐
│  01-DocumentPipeline / DealerFormExtractor.cs                │  MODULE 09
│                                                              │
│  Azure Document Intelligence (custom model: jma-incentive)   │
│  Extracts: DealerId, VehicleVin, ProgramCode, Amount, Date  │
│                                                              │
│  Per-field confidence scoring                                │
│                ┌────────────────────────────────────────┐   │
│  Confidence    │ ≥ 0.90  → Route: AUTO PROCESS          │   │
│  Routing       │ 0.70-0.89 → Route: HUMAN REVIEW QUEUE  │   │
│                │ < 0.70  → Route: DEAD LETTER QUEUE     │   │
│                └────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────┘
           |
           | ClaimRequest (auto route only)
           v
┌──────────────────────────────────────────────────────────────┐
│  03-AgentWorkflow / IncentiveClaimAgent.cs                   │  MODULE 06
│                                                              │
│  Semantic Kernel ReAct Loop                                  │
│  ┌────────────────────────────────────────────────────────┐  │
│  │  Kernel.CreateBuilder()                                │  │
│  │    .AddAzureOpenAIChatCompletion(gpt-4o)               │  │
│  │    .DefaultAzureCredential()                           │  │
│  │                                                        │  │
│  │  Plugins registered:                                   │  │
│  │    ✦ DealerEligibilityPlugin                           │  │
│  │    ✦ PolicyLookupPlugin  ──────────────────────────────┼──┼── calls HybridRetrieval
│  │    ✦ ClaimDecisionPlugin                               │  │
│  │                                                        │  │
│  │  AuditFilter (IFunctionInvocationFilter)               │  │
│  │    → logs every tool call + latency → App Insights     │  │
│  │                                                        │  │
│  │  System Prompt: 08-PromptEngineering/SystemPrompts.cs  │  │  GAP TOPIC
│  │    Identity → Scope → Rules → Format → Fallback        │  │
│  └────────────────────────────────────────────────────────┘  │
│                                                              │
│  ToolCallBehavior = AutoInvokeKernelFunctions                │
│  MaxAutoInvokeAttempts = 10  |  Temperature = 0.0            │
└──────────────────────────────────────────────────────────────┘
           |
           | LLM decides tool sequence (ReAct loop):
           |
     ┌─────┴───────────────────────────────────────────────┐
     │              TOOL CALLS (in order)                  │
     └─────────────────────────────────────────────────────┘
           |
           | STEP 1
           v
  ┌──────────────────────────────────────────────────────┐
  │  Plugin: DealerEligibilityPlugin.cs                  │  MODULE 06
  │  KernelFunction: check_dealer_eligibility            │
  │                                                      │
  │  → calls DMS API via HttpClient + Managed Identity   │
  │  → returns: DealerEligibilityResult                  │
  │    (IsEligible, ProgramsEnrolled, DealerStatus)      │
  │                                                      │
  │  ↕ Fault Tolerance Layer (07-FaultTolerance)         │  MODULE 10
  │    RetryPolicy.cs: 3x exponential backoff + jitter   │
  │    CircuitBreaker.cs: open after 5 fails in 30s      │
  │    → circuit open → EscalationService → RSM email    │
  └──────────────────────────────────────────────────────┘
           |
           | If eligible → STEP 2
           v
  ┌──────────────────────────────────────────────────────┐
  │  Plugin: PolicyLookupPlugin.cs                       │  MODULE 06 + GAP
  │  KernelFunction: lookup_incentive_policy             │
  │                                                      │
  │  → embeds query (same model as indexing!)            │
  │  → 02-RAGSearch / HybridRetrieval.cs                 │
  │                                                      │
  │    ┌────────────────────────────────────────────┐    │
  │    │  Hybrid Retrieval Pipeline                 │    │
  │    │                                            │    │
  │    │  ① Vector Search (HNSW cosine)             │    │
  │    │     finds semantically similar chunks      │    │
  │    │                                            │    │
  │    │  ② BM25 Keyword Search (parallel)          │    │
  │    │     finds exact program code matches       │    │
  │    │                                            │    │
  │    │  ③ RRF Fusion                              │    │
  │    │     score = 1/(k+rank_vector)              │    │
  │    │           + 1/(k+rank_keyword)             │    │
  │    │                                            │    │
  │    │  ④ Semantic Re-ranker                      │    │
  │    │     cross-encoder re-scores by meaning     │    │
  │    │                                            │    │
  │    │  → returns top-5 PolicyChunk[]             │    │
  │    │    with citations (DocumentId + section)   │    │
  │    └────────────────────────────────────────────┘    │
  └──────────────────────────────────────────────────────┘
           |
           | policy chunks injected into LLM context
           v
  ┌──────────────────────────────────────────────────────┐
  │  LLM evaluates claim against retrieved policy        │  MODULE 06
  │  (Chain-of-Thought reasoning on policy evidence)     │  GAP TOPIC
  │  Few-shot examples in prompt → CoT pattern learned   │
  └──────────────────────────────────────────────────────┘
           |
           | STEP 3
           v
  ┌──────────────────────────────────────────────────────┐
  │  Plugin: ClaimDecisionPlugin.cs                      │  MODULE 06
  │  KernelFunction: submit_claim_decision               │
  │                OR escalate_claim                     │
  │                                                      │
  │  submit_claim_decision:                              │
  │    validates decision ∈ {approved, denied}           │
  │    writes to DMS via HttpClient                      │
  │    generates AuthNumber if approved                  │
  │                                                      │
  │  escalate_claim (if uncertain):                      │
  │    → 07-FaultTolerance / EscalationService.cs        │
  │    → RSM notified via email + Teams                  │
  │    → ticket ID logged for audit trail                │
  └──────────────────────────────────────────────────────┘
           |
           v
┌──────────────────────────────────────────────────────────────┐
│  COMPLEX CLAIMS → 04-MetaAgentOrchestration                  │  MODULE 07
│                                                              │
│  SupervisorAgent.cs orchestrates specialist sub-agents:      │
│                                                              │
│         SupervisorAgent                                      │
│              |                                               │
│     ┌────────┼─────────────────────┐                        │
│     |        |                     |                        │
│     v        v                     v                        │
│  Claim    Policy              Fraud                         │
│  Validator Checker            Detector                      │
│  (fast,   (RAG on           (duplicate VIN,                 │
│  cheap)    policy docs)      amount outlier,                │
│     |        |               freq anomaly)                  │
│     |        └─────────────── ┘                             │
│     |         (parallel — independent)                      │
│     └─────────────────┬───────────────────┘                 │
│                       |                                      │
│                 Supervisor synthesizes:                      │
│                  fraud > 0.75 → escalate                    │
│                  policy fail  → deny                        │
│                  all pass     → approve                     │
│                                                              │
│  A2A between agents: 05-A2ACommunication/                   │  MODULE 08
│    AgentMessage<TPayload> typed envelopes                    │
│    AgentBus: schema validate → audit log → route            │
└──────────────────────────────────────────────────────────────┘
           |
           v
┌──────────────────────────────────────────────────────────────┐
│  06-MCPHub / MCPToolRegistry.cs                              │  MODULE 05
│                                                              │
│  All tool access routes through MCP Hub:                     │
│    ✦ Agents → Hub → DMS connector                           │
│    ✦ Agents → Hub → Policy Search connector                 │
│    ✦ Agents → Hub → Document Intelligence connector         │
│                                                              │
│  APIMGateway.cs sits in front of Hub:                       │
│    JWT validation → Rate limiting (100/min) → Audit log     │
│    then routes to MCPToolRegistry                            │
│                                                              │
│  N agents × M tools = N+M connections (not N×M)            │
└──────────────────────────────────────────────────────────────┘
           |
           v
  ┌──────────────────────────────────────────┐
  │  FINAL DECISION returned to caller       │
  │  {status, rationale, policy_ref,         │
  │   auth_number}                           │
  └──────────────────────────────────────────┘
```

---

## PATH C: CONTINUOUS QUALITY (LLMOps — runs in background)

```
Every production claim decision
           |
           v
  ┌────────────────────────────────────────────────────────┐
  │  09-LLMOps / GroundednessMonitor.cs                    │  GAP + MODULE 11
  │                                                        │
  │  GPT-4o evaluator judges each response:                │
  │    Is the decision grounded in retrieved chunks?        │
  │    Score: 0.0 (hallucinated) → 1.0 (fully grounded)    │
  │                                                        │
  │  ≥ 0.85  → log OK to App Insights                     │
  │  0.70-0.84 → WARNING alert to on-call engineer        │
  │  < 0.70  → CRITICAL — page on-call + investigate      │
  └────────────────────────────────────────────────────────┘

Every prompt change (CI/CD pipeline)
           |
           v
  ┌────────────────────────────────────────────────────────┐
  │  09-LLMOps / EvaluationPipeline.cs                     │  GAP + MODULE 11
  │                                                        │
  │  100 golden test cases → agent → score                 │
  │                                                        │
  │  Quality Gate (blocks deployment if fails):            │
  │    Groundedness ≥ 0.85                                 │
  │    Relevance    ≥ 0.80                                 │
  │    Decision Accuracy ≥ 0.90                            │
  │                                                        │
  │  PASS → merge PR → deploy to staging → canary 5%      │
  │  FAIL → PR blocked, prompt author fixes and retries    │
  └────────────────────────────────────────────────────────┘

Prompts stored in Git (PromptVersioning.cs)
  → YAML files, version history, changelog
  → CI triggers eval on every prompt PR
  → Pin version in prod, never auto-update
```

---

## COMPLETE MODULE MAP

```
┌────────────────────────────────────────────────────────────────────────┐
│                                                                        │
│  PDF In                                                                │
│    │                                                                   │
│    ▼                                                                   │
│  [01-DocumentPipeline]──────────────────────────── MODULE 09 + GAP    │
│    DealerFormExtractor  →  confidence routing                          │
│    ChunkingStrategy     →  fixed / paragraph / parent-child            │
│    │                                                                   │
│    ▼  ClaimRequest                                                     │
│  [03-AgentWorkflow]─────────────────────────────── MODULE 06          │
│    IncentiveClaimAgent  →  SK ReAct loop                               │
│    AuditFilter          →  IFunctionInvocationFilter                   │
│    │                                                                   │
│    ├──[08-PromptEngineering]────────────────────── GAP                 │
│    │   SystemPrompts    →  5-component design + few-shot CoT           │
│    │                                                                   │
│    ├──[02-RAGSearch]────────────────────────────── GAP                 │
│    │   HNSWVectorSearch →  HNSW index config (m, efConstruction)       │
│    │   HybridRetrieval  →  vector + BM25 + RRF + semantic rerank       │
│    │                                                                   │
│    ├──[06-MCPHub]───────────────────────────────── MODULE 05          │
│    │   MCPToolRegistry  →  tool discovery + routing                    │
│    │   APIMGateway      →  JWT + rate limit + audit                    │
│    │                                                                   │
│    ├──[07-FaultTolerance]───────────────────────── MODULE 10          │
│    │   RetryPolicy      →  Polly exponential backoff + jitter          │
│    │   CircuitBreaker   →  open → fast-fail → half-open → recover      │
│    │   EscalationService → RSM email + Teams notify                   │
│    │                                                                   │
│    └──[04-MetaAgentOrchestration]──────────────── MODULE 07          │
│        SupervisorAgent      →  delegate + synthesize                  │
│        ClaimValidatorAgent  →  fast rule-based check                  │
│        PolicyCheckerAgent   →  RAG-based policy eval                  │
│        FraudDetectorAgent   →  anomaly scoring                        │
│          │                                                             │
│          └──[05-A2ACommunication]──────────────── MODULE 08          │
│              AgentMessage   →  typed envelopes + schema version        │
│              AgentBus       →  validate + audit + route + dead-letter  │
│                                                                        │
│    ▼  Decision                                                         │
│  [09-LLMOps]────────────────────────────────────── GAP + MODULE 11   │
│    EvaluationPipeline      →  CI quality gate (pre-deploy)            │
│    GroundednessMonitor      →  live scoring (post-deploy)             │
│    PromptVersioning         →  Git-tracked, pinned in prod            │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```
