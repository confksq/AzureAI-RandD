# Azure AI Foundry vs Amazon Bedrock — 15-Dimension Comparison

> **Currency warning.** This reflects the platforms as of **2026-07**. Both ship
> monthly; model catalogs and regional availability in particular go stale fast.
> Treat the *structural* differences (billing model, identity model, how RAG and
> agents are assembled) as durable, and re-verify any specific model, region, or
> price before quoting it.

---

## Summary table

| # | Dimension | Azure AI Foundry | Amazon Bedrock | Practical edge |
|---|---|---|---|---|
| 1 | **Model selection** | Very broad catalog: OpenAI (exclusive), Anthropic, Meta, Mistral, Cohere, Phi, plus a large Hugging Face surface | Curated: Anthropic, Amazon Nova/Titan, Meta, Mistral, Cohere, AI21, Stability | **Azure** on breadth; Bedrock's smaller catalog is easier to govern |
| 2 | **RAG support** | Azure AI Search with integrated vectorization, hybrid retrieval, semantic reranker; "on your data" wiring | Knowledge Bases: managed chunk/embed/retrieve over OpenSearch Serverless, Aurora pgvector, Pinecone, Redis | **Azure** — the reranker is the single biggest out-of-the-box quality lever |
| 3 | **Agent framework** | Foundry Agent Service; Semantic Kernel and AutoGen as first-party SDKs | Bedrock Agents / AgentCore; Strands Agents SDK | **Even** — Azure is more mature in-process, AWS stronger on managed runtime |
| 4 | **Pricing model** | Pay-per-token, or Provisioned Throughput Units (PTU) for reserved capacity; Claude billed via Microsoft Marketplace at standard API rates | Pay-per-token, Provisioned Throughput in model units (hourly, committed term), Batch at a discount | **Even** — both punish idle reserved capacity; Bedrock Batch is the cheaper bulk path |
| 5 | **Security / compliance** | Entra ID, private endpoints, VNet injection, customer-managed keys, Purview integration | IAM, PrivateLink, VPC endpoints, KMS, CloudTrail | **Even** — the real question is which identity plane your org already runs |
| 6 | **Fine-tuning** | Broad: OpenAI models plus much of the open-weight catalog; LoRA and full fine-tuning paths | Select models only (Nova/Titan, Llama, Cohere); continued pre-training available | **Azure** on coverage; neither offers Claude fine-tuning |
| 7 | **Evaluation tools** | Foundry Evaluations — groundedness, relevance, coherence, fluency, similarity, safety evaluators; CI-runnable | Model Evaluation (automatic + human workflows); RAG evaluation for Knowledge Bases | **Azure** — richer built-in metric set and better pipeline integration |
| 8 | **SDK / API** | `azure-ai-projects`, `azure-ai-inference`, the OpenAI SDK for OpenAI models, Semantic Kernel; REST | `boto3` / AWS SDKs (`bedrock`, `bedrock-runtime`); Anthropic's Bedrock client for Claude | **Bedrock** — one SDK, one auth model, consistent across every service |
| 9 | **Regional availability** | More total regions, but per-model availability varies sharply by region | Fewer regions; cross-region inference profiles improve availability at a residency cost | **Azure** on footprint — verify per model, not per platform |
| 10 | **Enterprise support** | Microsoft Enterprise Agreement, dedicated support tiers, FastTrack | AWS Enterprise Support, Solutions Architects, Enterprise Discount Program | **Even** — decided by existing commercial relationship |
| 11 | **Vector search** | Azure AI Search — first-party, hybrid (BM25 + vector) with RRF and a semantic reranker | OpenSearch Serverless is the default; Aurora pgvector, Pinecone, Redis, MongoDB also supported | **Azure** — hybrid + reranker in one managed service is hard to match |
| 12 | **Observability** | Azure Monitor / Application Insights, OpenTelemetry-based Foundry tracing | CloudWatch metrics and logs, CloudTrail audit, S3 model-invocation logging, AgentCore observability | **Even** — Azure better for prompt-level tracing, AWS better for audit |
| 13 | **Open-source models** | Large catalog, serverless endpoints and managed-compute deployment | Smaller curated set; SageMaker JumpStart covers the long tail separately | **Azure** — one plane for both hosted and open-weight models |
| 14 | **Responsible AI** | Azure AI Content Safety: prompt shields, groundedness detection, protected-material detection | Bedrock Guardrails: content filters, denied topics, word filters, PII redaction, contextual grounding, Automated Reasoning checks | **Bedrock** — Guardrails attach per invocation, and Automated Reasoning checks have no Azure equivalent |
| 15 | **Integration ecosystem** | Microsoft 365, Fabric, Power Platform, Logic Apps, Purview, Dataverse | Lambda, Step Functions, S3, SageMaker, Kendra, Connect, EventBridge | **Whichever cloud already holds your data** — this dimension is decided for you |

---

## Where the differences actually bite

**Model access is a second gate on Bedrock.** IAM permission to call
`bedrock:InvokeModel` is not enough — each model must also be enabled in the
console, per account *and* per region. Azure's equivalent (deploy the model to
the project) is one step. Expect this to cost an afternoon the first time.

**Guardrails attach differently, and it matters architecturally.** Bedrock
Guardrails are a separate resource referenced by ID at invocation time, so one
guardrail can front many models — and an un-guardrailed call path is one omitted
parameter away. Azure Content Safety binds closer to the deployment, which is
less flexible but harder to bypass by accident. On Bedrock, enforce the
guardrail ID in a shared client wrapper rather than at each call site.

**The retrieval quality gap is real and under-discussed.** Azure AI Search's
semantic reranker is a second-stage cross-encoder over the initial result set.
In practice it moves answer quality more than swapping the generation model
does. Bedrock Knowledge Bases give you hybrid search but you assemble reranking
yourself. If a workload lives or dies on retrieval precision, that is a genuine
argument for Azure independent of model preference.

**One SDK versus several.** Bedrock is `boto3` for everything, with the same
credential chain, retry config, and error taxonomy as the rest of AWS. Azure
spreads across `azure-ai-projects`, `azure-ai-inference`, the OpenAI SDK, and
Semantic Kernel depending on which model and which capability. Azure's surface
is more capable; Bedrock's is faster to learn and to operate.

**Sampling parameters are a portability trap.** Current Claude models reject
`temperature` / `top_p` / `top_k` outright. Prompt portfolios and wrapper code
carrying those parameters from an older model — or from an OpenAI-shaped API —
fail validation on both platforms. Steer behavior with the system prompt.

---

## How to choose

| If this is true… | Go with |
|---|---|
| Your data is already in Azure Storage / Fabric / SharePoint | **Azure** — egress and latency decide it |
| Your data is already in S3 / Aurora / DynamoDB | **Bedrock** — same reason |
| You need OpenAI GPT models specifically | **Azure** — not available on Bedrock |
| Retrieval precision is the make-or-break requirement | **Azure** — the semantic reranker |
| You need formal verification of policy compliance | **Bedrock** — Automated Reasoning checks |
| Your team is deep in AWS IAM and boto3 | **Bedrock** — the operational learning curve is near zero |
| You need the widest open-weight model catalog | **Azure** |
| You want one identity plane with Microsoft 365 | **Azure** |

**The honest meta-answer:** for most organizations this is not a model-quality
decision. Both platforms serve the same frontier models. It is decided by data
gravity, existing identity infrastructure, and which commercial agreement you
already have. Teams that pick on benchmark scores usually end up paying for the
data movement they didn't model.

**On going multi-cloud:** two providers doubles the prompt portfolios,
evaluation runs, cost dashboards, and IAM models you maintain. It is worth it
for genuine concentration risk or an inherited workload. It is not worth it for
"vendor leverage" alone at small scale — the operational tax arrives immediately
and the negotiating benefit does not.
