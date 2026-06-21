# JMA Dealer Intelligence Platform — Flow with Loops and Conditions

---

## LEGEND

```
┌─────────────────────────────────────────────────────────────────────┐
│  ZONES                          CODE vs CONFIG                       │
│                                                                     │
│  ░░░  ZONE 1 — Pure Code        [CODE]   = written in .cs files     │
│       No LLM. No SK.            [CONFIG] = Azure portal / YAML /    │
│       HTTP calls, DB ops,                  appsettings.json          │
│       retry logic, routing      [BOTH]   = code defines it, Azure   │
│                                            stores/runs it            │
│  ▒▒▒  ZONE 2 — Semantic Kernel                                       │
│       Orchestrator only.        ──────── = normal flow              │
│       No reasoning.             ════════ = loop (goes back up)      │
│                                 ─ ─ ─ ─  = condition branch         │
│  ███  ZONE 3 — LLM / GPT-4o                                         │
│       Reasoning only.                                                │
│       Decides, interprets,                                           │
│       generates rationale                                            │
└─────────────────────────────────────────────────────────────────────┘
```

---

## PART 1 — ONE-TIME SETUP  (Run once when you deploy)

```
  Policy PDFs (incentive program documents)
       │
       ▼
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
  ░  STEP S1 — CHUNK THE DOCUMENTS                     ░  ZONE 1
  ░  File: 01-DocumentPipeline/ChunkingStrategy.cs     ░
  ░                                                    ░  [CODE]
  ░  Which strategy?                                   ░
  ░   ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─         ░
  ░  [IF policy doc] ──► paragraph chunking            ░
  ░       each paragraph = one complete policy rule    ░
  ░                                                    ░
  ░  [IF need precision + context] ──► parent-child    ░
  ░       child (200 tokens) → used for search         ░
  ░       parent (full section) → injected into LLM    ░
  ░                                                    ░
  ░  [IF homogeneous text] ──► fixed-size + overlap    ░
  ░       300 tokens, 50 token overlap                 ░
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
       │
       │  chunks[ ] — small text pieces
       ▼
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
  ░  STEP S2 — EMBED EACH CHUNK                        ░  ZONE 1
  ░  Azure OpenAI: text-embedding-3-large              ░
  ░  (This is NOT an LLM — it only converts           ░  [CONFIG] deploy model in Azure portal
  ░   text → float[1536]. No reasoning.)              ░  [CODE]   call from PolicyLookupPlugin
  ░                                                    ░
  ░  "Gulf Conquest max claim $5000" → [0.12, -0.34,  ░
  ░   0.87, ... 1536 numbers]                         ░
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
       │
       │  embeddings[ ]
       ▼
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
  ░  STEP S3 — BUILD HNSW INDEX                        ░  ZONE 1
  ░  File: 02-RAGSearch/HNSWVectorSearch.cs            ░
  ░                                                    ░  [BOTH]
  ░  Creates Azure AI Search index with:              ░  Code: CreateIndexAsync() defines schema
  ░    m = 4         (connections per node)            ░  Azure: stores and runs the index
  ░    efConstruction = 400  (build accuracy)          ░
  ░    efSearch = 500        (query accuracy)          ░
  ░    metric = cosine       (similarity measure)      ░
  ░                                                    ░
  ░  Also sets up:                                     ░
  ░    BM25 keyword index   (auto, built-in)           ░
  ░    Semantic re-ranker config                       ░
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
       │
       ▼
  [ INDEX READY — Azure AI Search now holds all policy knowledge ]

  ════════════════════════════════════════════════════
   Setup complete. Run once. The index stays forever.
   Re-run only when policy documents are updated.
  ════════════════════════════════════════════════════
```

---

## PART 2 — RUNTIME FLOW  (Every time a dealer submits a claim)

```
  Dealer submits claim PDF
       │
       ▼
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
  ░  STEP 1 — EXTRACT FROM PDF                               ░  ZONE 1
  ░  File: 01-DocumentPipeline/DealerFormExtractor.cs        ░
  ░                                                          ░  [CONFIG] Train custom DI model
  ░  Azure Document Intelligence                             ░           in DI Studio (UI)
  ░  Custom model: "jma-incentive-claim"                     ░  [CODE]   Call DI client,
  ░                                                          ░           parse confidence scores
  ░  Extracts: DealerId, VehicleVin, ProgramCode,           ░
  ░            ClaimAmount, SaleDate                         ░
  ░  Per-field confidence score (0.0 to 1.0)                ░
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
       │
       ▼
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
  ░  STEP 2 — CONFIDENCE ROUTING  (pure code, no LLM)       ░  ZONE 1
  ░  File: DealerFormExtractor.cs (same file)               ░
  ░                                                          ░  [CODE]
  ░  Take min confidence across all required fields          ░
  ░   ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─         ░
  ░  [IF min ≥ 0.90] ─────────────────► AUTO PROCESS ──────►│ continue to STEP 3
  ░                                                          ░
  ░  [IF 0.70 to 0.89] ──────────────► HUMAN REVIEW QUEUE   ░ stop here
  ░                       data entry team verifies manually  ░
  ░                       then re-enters → back to STEP 3   ░
  ░                                                          ░
  ░  [IF < 0.70] ─────────────────────► DEAD LETTER QUEUE   ░ stop here
  ░                       ops alerted, dealer asked to       ░
  ░                       resubmit with clearer form         ░
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
       │
       │  ClaimRequest { ClaimId, DealerId, VIN, ProgramCode, Amount, Date }
       │
  ════════════════════════════════════════════════════════════
   ▼   SEMANTIC KERNEL ENTERS HERE
  ════════════════════════════════════════════════════════════
       │
       ▼
  ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒
  ▒  STEP 3 — SK BUILDS THE KERNEL (runs once per claim)    ▒  ZONE 2
  ▒  File: 03-AgentWorkflow/IncentiveClaimAgent.cs          ▒
  ▒                                                         ▒  [CODE]
  ▒  Kernel.CreateBuilder()                                 ▒
  ▒    .AddAzureOpenAIChatCompletion("gpt-4o", endpoint,   ▒
  ▒       DefaultAzureCredential)     ← Managed Identity   ▒  [CONFIG] gpt-4o deployed
  ▒                                                         ▒           in Azure OpenAI portal
  ▒  Registers plugins (tools the LLM can call):           ▒
  ▒    ✦ DealerEligibilityPlugin                           ▒  [CODE]
  ▒    ✦ PolicyLookupPlugin                                ▒
  ▒    ✦ ClaimDecisionPlugin                               ▒
  ▒                                                         ▒
  ▒  Registers AuditFilter (IFunctionInvocationFilter)      ▒  [CODE]
  ▒    → fires before + after every tool call               ▒
  ▒    → logs tool name, latency, correlation ID            ▒  [CONFIG] App Insights
  ▒                                                         ▒           connection string
  ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒
       │
       ▼
  ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒
  ▒  STEP 4 — SK CREATES CHATHISTORY (the thread)          ▒  ZONE 2
  ▒  File: IncentiveClaimAgent.cs                          ▒
  ▒                                                         ▒  [CODE]
  ▒  ChatHistory = the rolling conversation log            ▒
  ▒                                                         ▒
  ▒  Loads from: 08-PromptEngineering/SystemPrompts.cs     ▒  [CODE]
  ▒                                                         ▒
  ▒  System prompt contains:                               ▒
  ▒    Identity  → "You are the JMA Incentive Claim Agent" ▒
  ▒    Scope     → "You process claims ONLY"               ▒
  ▒    Rules     → "Never approve without eligibility"     ▒
  ▒    Format    → "Always return JSON { status, reason }" ▒
  ▒    Fallback  → "If uncertain, escalate — never guess"  ▒
  ▒    Few-shot  → 2 worked examples showing CoT reasoning ▒
  ▒                                                         ▒
  ▒  Adds user message:                                    ▒
  ▒    "Process claim: DealerId=D1234, VIN=...,            ▒
  ▒     Program=GC-2026-Q1, Amount=$3200, Date=2026-01-15" ▒
  ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒
       │
       │  SK sends ChatHistory to LLM
       │
  ════════════════════════════════════════════════════════════
   ▼   LLM (GPT-4o) ENTERS HERE
   ▼   ┌─────────────────────────────────────────────────┐
       │  THE REACT LOOP  (repeats until no tool_call)   │
       │  Max 10 iterations (MaxAutoInvokeAttempts = 10) │
       └─────────────────────────────────────────────────┘
  ════════════════════════════════════════════════════════════
       │
  ┌────┴────────────────────────────────────────────────────┐
  │                                                         │
  │  ████████████████████████████████████████████████████  │
  │  █  STEP 5 — LLM DECIDES NEXT ACTION               █  │  ZONE 3
  │  █  Model: GPT-4o                                   █  │
  │  █                                                  █  │  [CONFIG] model deployed
  │  █  Reads everything in ChatHistory:                █  │           in Azure portal
  │  █    - system prompt (rules + examples)            █  │
  │  █    - claim details                               █  │
  │  █    - all previous tool results (if any)          █  │
  │  █                                                  █  │
  │  █  LLM outputs ONE of two things:                  █  │
  │  █                                                  █  │
  │  █   ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─           █  │
  │  █  [IF more info needed]                           █  │
  │  █    → outputs tool_call                           █  │
  │  █      { tool: "check_dealer_eligibility",         █  │
  │  █        params: { dealerId: "D1234" } }           █  │
  │  █                                                  █  │
  │  █  [IF all info gathered]                          █  │
  │  █    → outputs final text (no tool_call)           █  │
  │  █      "Claim approved. Criteria met per §3.2..."  █  │
  │  ████████████████████████████████████████████████████  │
  │          │                        │                    │
  │  [tool_call]              [final text — no tool]       │
  │          │                        │                    │
  │          ▼                        ▼                    │
  │  ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒     ┌───────────────┐           │
  │  ▒ STEP 6 — SK        ▒     │  EXIT LOOP    │           │
  │  ▒ INTERCEPTS         ▒     └───────┬───────┘           │
  │  ▒                    ▒             │                    │
  │  ▒  SK sees tool_call ▒             ▼ (go to STEP 11)   │
  │  ▒  AuditFilter fires ▒                                  │
  │  ▒  → logs BEFORE     ▒                                  │
  │  ▒  SK routes to      ▒                                  │
  │  ▒  correct plugin    ▒                                  │
  │  ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒                                  │
  │          │                                               │
  │          │  Which plugin?                                │
  │          │                                               │
  │     ─ ─ ─┼─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─               │
  │          │                                               │
  │     ┌────┴──────────────────────────────┐               │
  │     │         │                         │               │
  │     ▼         ▼                         ▼               │
  │                                                         │
  │  ░░░░░░░░░ ░░░░░░░░░░░░░░░░ ░░░░░░░░░░░░░░░░░░░░░       │
  │  ░ TOOL A ░ ░    TOOL B    ░ ░      TOOL C       ░       │
  │  ░        ░ ░              ░ ░                   ░       │
  │  ░ Dealer ░ ░    Policy    ░ ░  Claim Decision   ░  ZONE 1
  │  ░Eligibil░ ░    Lookup    ░ ░  (write or        ░
  │  ░  ity   ░ ░              ░ ░   escalate)        ░
  │  ░        ░ ░  Hybrid      ░ ░                   ░
  │  ░  DMS   ░ ░  Retrieval:  ░ ░ [IF approved]     ░
  │  ░  API   ░ ░  ① Embed     ░ ░  POST to DMS      ░
  │  ░  call  ░ ░    query     ░ ░  generate AuthNum  ░
  │  ░        ░ ░  ② HNSW     ░ ░                   ░
  │  ░[CODE]  ░ ░    vector    ░ ░ [IF denied]        ░
  │  ░        ░ ░    search    ░ ░  POST denial       ░
  │  ░WrappedIn ░  ③ BM25     ░ ░  cite policy ref   ░
  │  ░ Retry  ░ ░    keyword   ░ ░                   ░
  │  ░ Policy ░ ░    search    ░ ░ [IF uncertain]     ░
  │  ░ [CODE] ░ ░  ④ RRF      ░ ░  escalate_claim    ░
  │  ░        ░ ░    fusion    ░ ░  → EscalationSvc  ░
  │  ░Circuit ░ ░  ⑤ Semantic ░ ░  → RSM email+Teams ░
  │  ░Breaker ░ ░    rerank   ░ ░                   ░
  │  ░ [CODE] ░ ░             ░ ░ [CODE]             ░
  │  ░        ░ ░ Returns:    ░ ░                   ░
  │  ░Returns:░ ░ top-5 chunks░ ░ Returns:           ░
  │  ░ eligible░ ░ with source░ ░ { submitted: true, ░
  │  ░ /not   ░ ░ citations   ░ ░   authNumber }     ░
  │  ░░░░░░░░░ ░░░░░░░░░░░░░░░░ ░░░░░░░░░░░░░░░░░░░░░       │
  │     │              │                  │                  │
  │     └──────────────┴──────────────────┘                  │
  │                    │                                     │
  │                    │  tool result                        │
  │                    ▼                                     │
  │  ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒                       │
  │  ▒  STEP 7 — SK FEEDS RESULT BACK  ▒  ZONE 2            │
  │  ▒                                 ▒                     │
  │  ▒  AuditFilter fires              ▒  [CODE]             │
  │  ▒  → logs AFTER + latency (ms)   ▒                     │
  │  ▒  → writes to App Insights      ▒  [CONFIG]           │
  │  ▒                                 ▒                     │
  │  ▒  SK adds tool result to        ▒                     │
  │  ▒  ChatHistory                   ▒                     │
  │  ▒                                 ▒                     │
  │  ▒  SK sends updated ChatHistory  ▒                     │
  │  ▒  back to LLM                   ▒                     │
  │  ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒                       │
  │                    │                                     │
  │                    │  ◄═══════════════════════════════   │
  │                    ║  LOOP — goes back to STEP 5         │
  │                    ║  LLM reads new result and decides   │
  │                    ║  whether to call another tool       │
  │                    ║  or produce final answer            │
  │                    ╚═══════════════════════════════════► │
  │                    │                                     │
  └────────────────────┘  (loop runs max 10 times)          │
                                                             │
  ════════════════════════════════════════════════════════════
  TYPICAL LOOP SEQUENCE FOR A STANDARD CLAIM:
    Turn 1: LLM calls check_dealer_eligibility
    Turn 2: LLM reads eligibility → calls lookup_incentive_policy
    Turn 3: LLM reads 5 policy chunks → calls submit_claim_decision
    Turn 4: LLM reads submission result → produces final text
    Loop ends. 4 iterations.
  ════════════════════════════════════════════════════════════
       │
       │  Final text response (no tool_call)
       ▼
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
  ░  STEP 8 — SUPERVISOR HANDLES COMPLEX CLAIMS           ░  ZONE 1+2+3
  ░  File: 04-MetaAgentOrchestration/SupervisorAgent.cs   ░
  ░                                                        ░  [CODE]
  ░  [IF claim is complex / fraud risk / high value]       ░
  ░    SupervisorAgent delegates to 3 sub-agents           ░
  ░                                                        ░
  ░  ClaimValidatorAgent  (ZONE 1 — pure code, no LLM)    ░
  ░    fast rule-based checks: VIN length, amount > 0,    ░
  ░    date not in future, required fields present         ░
  ░    runs FIRST (cheap guard, fail fast)                 ░
  ░                                                        ░
  ░  PolicyCheckerAgent + FraudDetectorAgent               ░
  ░    run IN PARALLEL (independent of each other)         ░
  ░    each has its own: SK Kernel + LLM + system prompt   ░
  ░    each is its own mini ReAct loop (ZONE 2+3)          ░
  ░                                                        ░
  ░  A2A Messages between agents:                          ░
  ░  File: 05-A2ACommunication/AgentMessage.cs            ░
  ░    Typed envelope: MessageId, CorrelationId,          ░
  ░    SchemaVersion, Sender, Receiver, Payload           ░
  ░  File: AgentBus.cs                                     ░
  ░    validates schema → logs audit → routes to agent    ░
  ░    → dead letter if undeliverable                     ░
  ░                                                        ░
  ░  All tool calls route through:                         ░
  ░  File: 06-MCPHub/MCPToolRegistry.cs                    ░
  ░    Agents → Hub → correct tool connector              ░
  ░    N agents × M tools = N+M (not N×M connections)     ░
  ░  File: APIMGateway.cs                                  ░
  ░    JWT validate → rate limit → audit → route          ░
  ░                                                        ░
  ░  Supervisor synthesizes results:                       ░
  ░    fraud score > 0.75  → escalate (RSM)               ░
  ░    policy not met      → deny (cite reason)           ░
  ░    all pass            → approve                      ░
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
       │
       ▼
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
  ░  STEP 9 — FAULT TOLERANCE (wraps every external call) ░  ZONE 1
  ░  File: 07-FaultTolerance/RetryPolicy.cs               ░
  ░  File: 07-FaultTolerance/CircuitBreaker.cs            ░
  ░                                                        ░  [CODE]  Polly library
  ░  Every DMS / Search / external API call goes through: ░
  ░                                                        ░
  ░  RetryPolicy:                                          ░
  ░    fail → wait 1s → retry                             ░
  ░    fail → wait 2s → retry (exponential)               ░
  ░    fail → wait 4s → retry + jitter                    ░
  ░    still fail → pass to CircuitBreaker                ░
  ░                                                        ░
  ░  CircuitBreaker:                                       ░
  ░    5 failures in 30s → OPEN (fast-fail 60s)           ░
  ░    after 60s → HALF-OPEN (test 1 call)                ░
  ░    [IF success] → CLOSED (normal operation)           ░
  ░    [IF fail again] → OPEN again (reset timer)         ░
  ░                                                        ░
  ░  EscalationService (when circuit stays open):         ░
  ░  File: EscalationService.cs                           ░
  ░    generate ticket ID                                  ░
  ░    email RSM + Teams message with full context        ░
  ░    agent logs ticket ID for audit trail               ░
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
       │
       ▼
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
  ░  STEP 10 — OUTPUT VALIDATION (pure code)              ░  ZONE 1
  ░  File: IncentiveClaimAgent.cs                         ░
  ░                                                        ░  [CODE]
  ░  [IF status ∈ {approved, denied, escalated}]          ░
  ░    → return response to caller                        ░
  ░                                                        ░
  ░  [IF invalid / unexpected output]                     ░
  ░    → escalate to RSM (never guess)                    ░
  ░    → log anomaly to App Insights                      ░
  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
       │
       ▼
  DECISION RETURNED  { status, rationale, policy_ref, auth_number }
```

---

## PART 3 — CONTINUOUS QUALITY  (Runs in background, always on)

```
  Every production claim decision (async — does NOT block the response)
       │
       ▼
  ████████████████████████████████████████████████████████
  █  STEP Q1 — GROUNDEDNESS MONITOR                     █  ZONE 3
  █  File: 09-LLMOps/GroundednessMonitor.cs             █
  █                                                      █  [CODE]  scoring logic
  █  A SECOND GPT-4o call (evaluator, not decider):     █  [CONFIG] App Insights
  █  "Is the rationale supported by the chunks?         █           dashboard
  █   Or did the LLM make things up?"                   █
  █                                                      █
  █  ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─         █
  █  [IF score ≥ 0.85] → log OK to App Insights        █
  █                                                      █
  █  [IF 0.70 to 0.84] → WARNING                        █
  █    alert on-call engineer                           █
  █    investigate: did policy docs change?             █
  █                prompt drift? chunking issue?        █
  █                                                      █
  █  [IF score < 0.70] → CRITICAL                       █
  █    page on-call immediately                         █
  █    all claims in last hour flagged for RSM review   █
  ████████████████████████████████████████████████████████

  ════════════════════════════════════════════════════════
  Every time a developer changes a system prompt (CI/CD)
  ════════════════════════════════════════════════════════
       │
       ▼
  ████████████████████████████████████████████████████████
  █  STEP Q2 — EVALUATION PIPELINE (CI quality gate)    █  ZONE 3
  █  File: 09-LLMOps/EvaluationPipeline.cs              █
  █                                                      █  [CODE]  pipeline logic
  █  100 golden test cases (known correct answers)      █  [CONFIG] Azure DevOps
  █  → run each through the agent                       █           pipeline YAML
  █  → score each response with GPT-4o as judge        █
  █                                                      █
  █  Quality Gate — ALL must pass to allow deployment:  █
  █    Groundedness  ≥ 0.85                             █
  █    Relevance     ≥ 0.80                             █
  █    Decision accuracy ≥ 0.90                         █
  █                                                      █
  █  ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─         █
  █  [IF all pass] → PR can merge → deploy to staging  █
  █                  canary 5% traffic → promote to prod█
  █                                                      █
  █  [IF any fail] → PR blocked                         █
  █                  developer fixes prompt             █
  █                  re-runs CI  ◄══ LOOP               █
  ████████████████████████████████████████████████████████

  File: 09-LLMOps/PromptVersioning.cs   [CODE + CONFIG]
    Prompts stored as YAML files in Git (version controlled)
    Every prompt change = PR with eval run
    Pin exact version in production (never auto-update)
    Roll back = git revert = instant
```

---

## SUMMARY: CODE vs CONFIG for Every Module

```
┌──────────────────────────────────┬─────────────┬──────────────────────────────────────┐
│  What                            │  Code/Config│  Details                             │
├──────────────────────────────────┼─────────────┼──────────────────────────────────────┤
│ DI custom model training         │  CONFIG     │  Done in DI Studio (Azure portal UI) │
│ DI client call + confidence logic│  CODE       │  DealerFormExtractor.cs              │
│ Chunking strategies              │  CODE       │  ChunkingStrategy.cs                 │
│ Embedding call                   │  CODE       │  Called inside PolicyLookupPlugin.cs │
│ text-embedding-3-large deploy    │  CONFIG     │  Azure OpenAI portal                 │
│ HNSW index schema                │  BOTH       │  Code: CreateIndexAsync()            │
│   (m, efConstruction, metric)    │             │  Azure: stores + runs the index      │
│ BM25 + semantic rerank           │  CONFIG     │  Enabled when creating index in      │
│                                  │             │  Azure AI Search (auto on hybrid)    │
│ Hybrid retrieval logic           │  CODE       │  HybridRetrieval.cs                  │
│ SK Kernel setup                  │  CODE       │  IncentiveClaimAgent.cs              │
│ gpt-4o model deployment          │  CONFIG     │  Azure OpenAI portal                 │
│ Plugin registration              │  CODE       │  IncentiveClaimAgent.cs              │
│ AuditFilter (log tool calls)     │  CODE       │  AuditFilter.cs                      │
│ App Insights workspace           │  CONFIG     │  Azure portal                        │
│ System prompt design             │  CODE       │  SystemPrompts.cs                    │
│ MaxAutoInvokeAttempts = 10       │  CODE       │  OpenAIPromptExecutionSettings       │
│ Temperature = 0.0                │  CODE       │  OpenAIPromptExecutionSettings       │
│ DealerEligibilityPlugin          │  CODE       │  Plugin .cs file                     │
│ PolicyLookupPlugin               │  CODE       │  Plugin .cs file                     │
│ ClaimDecisionPlugin              │  CODE       │  Plugin .cs file                     │
│ RetryPolicy (3x + backoff)       │  CODE       │  RetryPolicy.cs (Polly)              │
│ CircuitBreaker (5 fail / 30s)    │  CODE       │  CircuitBreaker.cs (Polly)           │
│ EscalationService (email/Teams)  │  CODE       │  EscalationService.cs                │
│ SMTP / Teams webhook             │  CONFIG     │  appsettings.json / Key Vault        │
│ SupervisorAgent                  │  CODE       │  SupervisorAgent.cs                  │
│ Sub-agents (Validator, Policy,   │  CODE       │  3 separate .cs agent files          │
│   Fraud)                         │             │                                      │
│ A2A typed message schema         │  CODE       │  AgentMessage.cs                     │
│ AgentBus routing + audit         │  CODE       │  AgentBus.cs                         │
│ MCPToolRegistry (tool discovery) │  CODE       │  MCPToolRegistry.cs                  │
│ APIMGateway (JWT + rate limit)   │  BOTH       │  Code: APIMGateway.cs                │
│                                  │             │  Config: APIM policies in portal     │
│ GroundednessMonitor              │  CODE       │  GroundednessMonitor.cs              │
│ EvaluationPipeline               │  CODE       │  EvaluationPipeline.cs               │
│ CI pipeline YAML                 │  CONFIG     │  azure-pipelines.yml                 │
│ PromptVersioning (Git tracking)  │  BOTH       │  Code: loader + validator            │
│                                  │             │  Config: YAML prompt files in Git    │
│ DefaultAzureCredential           │  CODE       │  All files using Azure services      │
│ Managed Identity assignment      │  CONFIG     │  Azure portal → IAM roles            │
└──────────────────────────────────┴─────────────┴──────────────────────────────────────┘
```
